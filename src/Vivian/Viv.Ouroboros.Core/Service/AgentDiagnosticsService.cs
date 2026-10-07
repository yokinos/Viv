using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Engine;
using Viv.Entity.Database.Ouroboros;
using Viv.Entity.Enums;
using Viv.EventContracts.Ouroboros;
using Viv.Ouroboros.Core.Entity.Dto.Agent;
using Viv.Ouroboros.Core.Entity.Vo.Agent;
using Viv.Ouroboros.Core.IService;
using Viv.Outbox;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// 自检实现：档位解析、Agent 装配、真实对话、清缓存、投递一轮到队列
    /// </summary>
    public class AgentDiagnosticsService : IAgentDiagnosticsService, IDependency
    {
        private readonly IModelProfileProvider _profiles;
        private readonly IAgentFactory _agents;
        private readonly IConfigChangeNotifier _notifier;
        private readonly IAgentStore _store;
        private readonly IVivContext _context;
        private readonly IVivOutbox _outbox;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="profiles">模型档位</param>
        /// <param name="agents">Agent 装配</param>
        /// <param name="notifier">配置变更通知</param>
        /// <param name="store">会话与消息库访问</param>
        /// <param name="context">请求上下文（取主体做越权校验）</param>
        /// <param name="outbox">发件箱（入队时打上下文快照，投递交给 Worker）</param>
        public AgentDiagnosticsService(IModelProfileProvider profiles, IAgentFactory agents, IConfigChangeNotifier notifier,
            IAgentStore store, IVivContext context, IVivOutbox outbox)
        {
            _profiles = profiles;
            _agents = agents;
            _notifier = notifier;
            _store = store;
            _context = context;
            _outbox = outbox;
        }

        /// <summary>
        /// 看某个档位解析成什么（不回密钥，只回有没有取到）
        /// </summary>
        public async Task<VivApiResult> GetProfileAsync(string profileKey)
        {
            var profile = await _profiles.GetProfileAsync(profileKey);
            if (profile is null) return VivApiResult.Failed($"档位不可用：{profileKey}");

            return VivApiResult.Success(new ModelProfileOutput
            {
                ProfileKey = profile.ProfileKey,
                Model = profile.Model,
                ApiUrl = profile.ApiUrl,
                HasKey = !string.IsNullOrEmpty(profile.ApiKey),
                Temperature = profile.Temperature,
                MaxOutputTokens = profile.MaxOutputTokens,
                Priority = profile.Priority
            });
        }

        /// <summary>
        /// 看某个 Agent 能否装配出来
        /// </summary>
        public async Task<VivApiResult> GetAgentAsync(string agentKey)
        {
            var agent = await _agents.GetAgentAsync(agentKey);
            if (agent is null) return VivApiResult.Failed($"Agent 未就绪：{agentKey}");

            return VivApiResult.Success(new AgentInfoOutput
            {
                Id = agent.Id,
                Name = agent.Name,
                Description = agent.Description
            });
        }

        /// <summary>
        /// 真跑一次对话（会消耗 token）
        /// </summary>
        public async Task<VivApiResult> ChatAsync(string agentKey, string text)
        {
            var agent = await _agents.GetAgentAsync(agentKey);
            if (agent is null) return VivApiResult.Failed($"Agent 未就绪：{agentKey}");

            var response = await agent.RunAsync(text);
            return VivApiResult.Success(new ChatTurnOutput
            {
                Text = response.Text,
                InputTokens = (int?)response.Usage?.InputTokenCount,
                OutputTokens = (int?)response.Usage?.OutputTokenCount
            });
        }

        /// <summary>
        /// 落一条用户消息并入发件箱：这里不跑模型，跑模型的是 Worker 消费者。
        /// 走 IVivOutbox 而不是 IVivEventPublisher —— 后者要本进程配齐 NanaOption、起一个 MQ 宿主，
        /// 而 Api 只写不投（OutboxOption.EnableDispatcher = false，投递交给 Worker），配 MQ 纯属多余；
        /// 发件箱入队只用业务主库，投递由 Worker 的投递器统一发出去。
        /// 上下文快照同样由入队那条路打进信封，Worker 侧主体不会丢。
        /// 请求带 UserMessageId 时不新建消息，只把同一条事件重投一次（幂等验证用）。
        /// </summary>
        public async Task<VivApiResult> QueueTurnAsync(QueueTurnRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.ConversationKey == Guid.Empty) return VivApiResult.Failed("conversationKey 不能为空");

            var conversation = await _store.GetConversationAsync(request.ConversationKey);
            if (conversation is null) return VivApiResult.Failed("会话不存在");
            if (conversation.SubjectId != _context.SubjectId) return VivApiResult.Failed("无权访问该会话");
            if (conversation.Status != EmConversationStatus.Active) return VivApiResult.Failed("会话已结束");

            long userMessageId;
            string text;

            if (request.UserMessageId is { } existingId)
            {
                // 重投模式：认领已落库的那条用户消息，投出的事件内容与首次投递逐字一致
                var existing = (await _store.ListMessagesAsync(conversation.Id)).FirstOrDefault(x => x.Id == existingId);
                if (existing is null) return VivApiResult.Failed($"用户消息不存在：{existingId}");

                userMessageId = existing.Id;
                text = existing.Content ?? string.Empty;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(request.Text)) return VivApiResult.Failed("text 不能为空");

                var userMessage = new OtMessage
                {
                    ConversationId = conversation.Id,
                    Seq = await _store.NextSeqAsync(conversation.Id),
                    Role = EmMessageRole.User,
                    Content = request.Text,
                    ContentType = EmMessageContentType.Text,
                    AgentKey = conversation.MainAgentKey
                };

                // 先落库再投递：投出去的消息必须已经能在库里被 Worker 读到，否则消费端定位不到这一轮
                if (!await _store.InsertMessageAsync(userMessage)) return VivApiResult.Failed("写入用户消息失败");

                userMessageId = userMessage.Id;
                text = request.Text;
            }

            var enqueued = await _outbox.EnqueueAsync(new OuroborosTurnEvent
            {
                ConversationKey = request.ConversationKey,
                UserMessageId = userMessageId,
                Text = text
            }, cancellationToken);

            if (!enqueued) return VivApiResult.Failed("写入发件箱失败");

            return VivApiResult.Success(new QueueTurnOutput { Ok = true, UserMessageId = userMessageId });
        }

        /// <summary>
        /// 清缓存：改完库里的档位、Agent 定义或工具绑定时调用，不必等 TTL。
        /// 与各管理接口的写操作走同一个失效入口（<see cref="IConfigChangeNotifier"/>），口径只有一份。
        /// </summary>
        public VivApiResult Refresh(string? agentKey, string? profileKey)
        {
            // 本进程先按范围清；不带 agentKey 时它内部会两边全清（Agent 装配里含着提示词与工具列表），
            // 再写共享版本戳让其它实例在节流窗口内自行清缓存
            _notifier.Notify(agentKey, profileKey);

            return VivApiResult.Success();
        }
    }
}
