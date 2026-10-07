using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Viv.Contracts.Interface;
using Viv.Engine;
using Viv.Entity.Database.Ouroboros;
using Viv.Entity.Enums;
using Viv.Log;
using Viv.Ouroboros.Core.Entity.Dto.Agent;
using Viv.Ouroboros.Core.Entity.Vo.Agent;
using Viv.Ouroboros.Core.IService;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// 对话服务实现。
    ///
    /// 一轮的完整动作：会话串行锁 → 恢复 AgentSession 快照 → 跑一轮 → 落消息与用量 → 存回快照
    /// → 若模型调了需审批的工具，落一张待审批单并把 RequestId 返回给前端。
    ///
    /// 锁是**可选依赖**：IDistributedLock 只在配了 Redis 时注册，没有就只记 Warning 继续跑
    /// （同一会话并发两轮会互相覆盖会话状态，生产必须配 Redis）。
    /// </summary>
    public class AgentChatService : IAgentChatService, IDependency
    {
        private static readonly TimeSpan LockExpire = TimeSpan.FromMinutes(5);

        private readonly IAgentStore _store;
        private readonly IAgentFactory _agents;
        private readonly IVivContext _context;
        private readonly IServiceProvider _services;
        private readonly TokenUsageRecorder _usage;
        private readonly ILoggerContract _logger;

        public AgentChatService(
            IAgentStore store,
            IAgentFactory agents,
            IVivContext context,
            IServiceProvider services,
            TokenUsageRecorder usage,
            ILoggerContract logger)
        {
            _store = store;
            _agents = agents;
            _context = context;
            _services = services;
            _usage = usage;
            _logger = logger;
        }

        public async Task<VivApiResult> SendAsync(Guid conversationKey, SendMessageRequest request, CancellationToken cancellationToken = default)
        {
            var (conversation, agent, error) = await PrepareAsync(conversationKey);
            if (error is not null) return VivApiResult.Failed(error);

            var distributedLock = _services.GetService<IDistributedLock>();
            ChatTurnResult result;

            if (distributedLock is null)
            {
                _logger.Warning("未配置 Redis，会话未加锁：并发两轮会互相覆盖会话状态");
                result = await RunTurnAsync(conversation!, agent!, request.Text, conversationKey, cancellationToken);
            }
            else
            {
                result = await distributedLock.AcquireLockWithExecuteAsync(
                    $"ouroboros:chat:{conversationKey}",
                    LockExpire,
                    () => RunTurnAsync(conversation!, agent!, request.Text, conversationKey, cancellationToken),
                    () => Task.FromResult(new ChatTurnResult(null, null, null, null, "该会话正在处理上一条消息，请稍后再发")),
                    cancellationToken: cancellationToken);
            }

            return ToResult(result);
        }

        public async Task<VivApiResult> ListPendingApprovalsAsync()
        {
            var rows = await _store.ListPendingApprovalsAsync(_context.SubjectId);
            return VivApiResult.Success(rows.Select(x => new ApprovalItemOutput
            {
                ApprovalId = x.ApprovalId,
                ConversationId = x.ConversationId,
                ToolKey = x.ToolKey,
                Arguments = x.Arguments,
                RequestedAt = x.RequestedAt,
                ExpiresAt = x.ExpiresAt
            }).ToList());
        }

        public async IAsyncEnumerable<string> StreamAsync(Guid conversationKey, SendMessageRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var text = request.Text;
            var (conversation, agent, error) = await PrepareAsync(conversationKey);
            if (error is not null)
            {
                yield return $"[错误] {error}";
                yield break;
            }

            var session = await RestoreSessionAsync(agent!, conversation!.Id, conversation.MainAgentKey);
            var seq = await _store.NextSeqAsync(conversation.Id);

            var userMessage = new OtMessage
            {
                ConversationId = conversation.Id,
                Seq = seq,
                Role = "user",
                Content = text,
                ContentType = "text",
                AgentKey = conversation.MainAgentKey
            };
            await _store.InsertMessageAsync(userMessage);

            // 流式这一轮也要带上会话上下文：工具/子 Agent 留痕从 AsyncLocal 读，退出即还原
            using var turn = AgentTurnContext.Enter(conversation.Id, userMessage.Id, conversation.MainAgentKey);
            var full = new StringBuilder();

            await foreach (var update in agent!.RunStreamingAsync(text, session, cancellationToken: cancellationToken))
            {
                if (string.IsNullOrEmpty(update.Text)) continue;
                full.Append(update.Text);
                yield return update.Text;
            }

            // 流结束后落库与快照（客户端中途断开时这一段落不到，属已知取舍）
            var reply = full.ToString();
            await _store.InsertMessageAsync(new OtMessage
            {
                ConversationId = conversation.Id,
                Seq = seq + 1,
                Role = "assistant",
                Content = reply,
                ContentType = "text",
                AgentKey = conversation.MainAgentKey
            });

            conversation.MessageCount += 2;
            conversation.LastMessageAt = DateTime.Now;
            await _store.UpdateConversationAsync(conversation);

            await SaveSessionAsync(agent, session, conversation.Id, conversation.MainAgentKey);
        }

        /// <summary>
        /// 审批并续跑被暂停的那一轮
        /// </summary>
        public async Task<VivApiResult> DecideApprovalAsync(bool approved, ApprovalDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var remark = request.Remark;
            var approval = await _store.GetApprovalAsync(request.ApprovalId);
            if (approval is null) return VivApiResult.Failed("审批请求不存在");
            if (approval.SubjectId != _context.SubjectId) return VivApiResult.Failed("无权处理该审批");
            if (approval.Status != EmApprovalStatus.Pending) return VivApiResult.Failed("该审批已处理");

            approval.Status = approved ? EmApprovalStatus.Approved : EmApprovalStatus.Rejected;
            approval.DecidedBy = _context.UserId;
            approval.DecidedAt = DateTime.Now;
            approval.DecisionRemark = remark;
            await _store.UpdateApprovalAsync(approval);

            var conversation = await _store.GetConversationAsync(await ResolveConversationKeyAsync(approval.ConversationId));
            if (conversation is null) return VivApiResult.Failed("会话不存在");

            var agent = await _agents.GetAgentAsync(conversation.MainAgentKey);
            if (agent is null) return VivApiResult.Failed("Agent 未就绪");

            if (string.IsNullOrEmpty(approval.ExternalRequestId))
                return VivApiResult.Failed("审批缺少 MAF 请求 id，无法续跑（请重新发起该轮）");

            var session = await RestoreSessionAsync(agent, conversation.Id, conversation.MainAgentKey);

            // 按真实签名重建：ToolApprovalResponseContent(requestId, approved, toolCall)
            var toolCall = new FunctionCallContent(approval.ExternalToolCallId ?? string.Empty, approval.ToolKey);
            var messages = new List<ChatMessage>
            {
                new(ChatRole.User, [new ToolApprovalResponseContent(approval.ExternalRequestId, approved, toolCall)])
            };

            // 续跑也是"跑一轮"：工具留痕同样要认得会话（续跑没有用户消息，MessageId 传 null）
            using var turn = AgentTurnContext.Enter(conversation.Id, null, conversation.MainAgentKey);
            var response = await agent.RunAsync(messages, session, cancellationToken: cancellationToken);
            return ToResult(await PersistTurnAsync(conversation, agent, session, response, cancellationToken));
        }

        #region 内部

        /// <summary>
        /// 内部结果 → 统一信封
        /// </summary>
        private static VivApiResult ToResult(ChatTurnResult result)
            => result.Error is not null
                ? VivApiResult.Failed(result.Error)
                : VivApiResult.Success(new ChatTurnOutput
                {
                    Text = result.Text,
                    InputTokens = result.InputTokens,
                    OutputTokens = result.OutputTokens,
                    PendingApprovalId = result.PendingApprovalId
                });

        private async Task<(OtConversation? conversation, AIAgent? agent, string? error)> PrepareAsync(Guid conversationKey)
        {
            var conversation = await _store.GetConversationAsync(conversationKey);
            if (conversation is null) return (null, null, "会话不存在");

            // 越权防线：subjectId 只认上下文，不认请求参数
            if (conversation.SubjectId != _context.SubjectId) return (null, null, "无权访问该会话");
            if (conversation.Status != EmConversationStatus.Active) return (null, null, "会话已结束");

            var agent = await _agents.GetAgentAsync(conversation.MainAgentKey);
            if (agent is null) return (null, null, $"Agent 未就绪：{conversation.MainAgentKey}");

            return (conversation, agent, null);
        }

        private async Task<ChatTurnResult> RunTurnAsync(OtConversation conversation, AIAgent agent, string text,
            Guid conversationKey, CancellationToken cancellationToken)
        {
            var session = await RestoreSessionAsync(agent, conversation.Id, conversation.MainAgentKey);
            var seq = await _store.NextSeqAsync(conversation.Id);

            var userMessage = new OtMessage
            {
                ConversationId = conversation.Id,
                Seq = seq,
                Role = "user",
                Content = text,
                ContentType = "text",
                AgentKey = conversation.MainAgentKey
            };
            await _store.InsertMessageAsync(userMessage);

            // 工具回调由 MAF 在 RunAsync 内部发起，且可能在别的线程上续跑：
            // 会话上下文只能靠 AsyncLocal 随 ExecutionContext 流过去，退出（Dispose）即还原
            using var turn = AgentTurnContext.Enter(conversation.Id, userMessage.Id, conversation.MainAgentKey);

            var response = await agent.RunAsync(text, session, cancellationToken: cancellationToken);
            return await PersistTurnAsync(conversation, agent, session, response, cancellationToken, seq);
        }

        private async Task<ChatTurnResult> PersistTurnAsync(OtConversation conversation, AIAgent agent, AgentSession session,
            AgentResponse response, CancellationToken cancellationToken, int userSeq = 0)
        {
            var seq = userSeq > 0 ? userSeq + 1 : await _store.NextSeqAsync(conversation.Id);

            await _store.InsertMessageAsync(new OtMessage
            {
                ConversationId = conversation.Id,
                Seq = seq,
                Role = "assistant",
                Content = response.Text,
                ContentType = "text",
                AgentKey = conversation.MainAgentKey,
                ModelProfile = null,
                InputTokens = response.Usage?.InputTokenCount is { } i ? (int)i : null,
                OutputTokens = response.Usage?.OutputTokenCount is { } o ? (int)o : null,
                CachedInputTokens = response.Usage?.CachedInputTokenCount is { } c ? (int)c : null,
                ReasoningTokens = response.Usage?.ReasoningTokenCount is { } r ? (int)r : null
            });

            conversation.MessageCount += userSeq > 0 ? 2 : 1;
            conversation.LastMessageAt = DateTime.Now;
            conversation.TotalInputTokens += response.Usage?.InputTokenCount ?? 0;
            conversation.TotalOutputTokens += response.Usage?.OutputTokenCount ?? 0;
            await _store.UpdateConversationAsync(conversation);

            await SaveSessionAsync(agent, session, conversation.Id, conversation.MainAgentKey);

            // 用量出账放在消息与会话都落完之后：出账失败不该回滚已经跑完的这一轮
            await _usage.RecordAsync(conversation.MainAgentKey, response.Usage, cancellationToken);

            // 模型要求人工审批 → 落单并把 RequestId 交给前端
            var pending = response.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>().FirstOrDefault();
            if (pending is null)
            {
                return new ChatTurnResult(response.Text, (int?)response.Usage?.InputTokenCount,
                    (int?)response.Usage?.OutputTokenCount, null);
            }

            var toolCall = pending.ToolCall as FunctionCallContent;
            var approval = new OtApproval
            {
                ApprovalId = Guid.NewGuid(),
                ExternalRequestId = pending.RequestId,
                ExternalToolCallId = toolCall?.CallId,
                ConversationId = conversation.Id,
                SubjectId = _context.SubjectId,
                ToolKey = toolCall?.Name ?? "unknown",
                Arguments = toolCall is null ? null : JsonSerializer.Serialize(toolCall.Arguments),
                Status = EmApprovalStatus.Pending,
                RequestedAt = DateTime.Now,
                ExpiresAt = DateTime.Now.AddHours(24)
            };
            await _store.InsertApprovalAsync(approval);

            _logger.Info("已产生待审批：{0}（会话 {1}）", approval.ToolKey, conversation.ConversationKey);

            return new ChatTurnResult(response.Text, (int?)response.Usage?.InputTokenCount,
                (int?)response.Usage?.OutputTokenCount, approval.ApprovalId);
        }

        private async Task<AgentSession> RestoreSessionAsync(AIAgent agent, long conversationId, string agentKey)
        {
            var state = await _store.GetSessionStateAsync(conversationId, agentKey);
            if (state is null || string.IsNullOrWhiteSpace(state.StateJson)) return await agent.CreateSessionAsync();

            try
            {
                using var document = JsonDocument.Parse(state.StateJson);
                return await agent.DeserializeSessionAsync(document.RootElement);
            }
            catch (Exception ex)
            {
                // 快照结构对不上（MAF 升级/手工改过）→ 从零开始，别把整轮对话打死
                _logger.Error("会话快照恢复失败，改为新建会话：{0}，{1}", conversationId, ex.Message);
                return await agent.CreateSessionAsync();
            }
        }

        private async Task SaveSessionAsync(AIAgent agent, AgentSession session, long conversationId, string agentKey)
        {
            try
            {
                var state = await agent.SerializeSessionAsync(session);
                await _store.SaveSessionStateAsync(conversationId, agentKey, state.GetRawText());
            }
            catch (Exception ex)
            {
                _logger.Error("会话快照保存失败：{0}，{1}", conversationId, ex.Message);
            }
        }

        /// <summary>审批单只存了 ConversationId，这里反查会话 Key（审批续跑需要）</summary>
        private async Task<Guid> ResolveConversationKeyAsync(long conversationId)
        {
            var conversations = await _store.ListConversationsAsync(_context.SubjectId, null, 1, 200);
            return conversations.FirstOrDefault(x => x.Id == conversationId)?.ConversationKey ?? Guid.Empty;
        }

        #endregion
    }
}
