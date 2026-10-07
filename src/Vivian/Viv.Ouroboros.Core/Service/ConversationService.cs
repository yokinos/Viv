using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    /// 会话管理实现：会话的增查、消息查询、结束
    /// </summary>
    public class ConversationService : IConversationService, IDependency
    {
        private readonly IAgentStore _store;
        private readonly IAgentFactory _agents;
        private readonly IVivContext _context;
        private readonly ILoggerContract _logger;

        public ConversationService(IAgentStore store, IAgentFactory agents, IVivContext context, ILoggerContract logger)
        {
            _store = store;
            _agents = agents;
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// 建会话：主 Agent 不存在或未启用则失败
        /// </summary>
        public async Task<VivApiResult> CreateAsync(CreateConversationRequest request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.MainAgentKey)) return VivApiResult.Failed("mainAgentKey 不能为空");

            var agent = await _agents.GetAgentAsync(request.MainAgentKey);
            if (agent is null)
            {
                _logger.Warning("建会话失败，Agent 未就绪：{0}", request.MainAgentKey);
                return VivApiResult.Failed($"Agent 未就绪：{request.MainAgentKey}");
            }

            var conversation = new OtConversation
            {
                ConversationKey = Guid.NewGuid(),
                SubjectId = _context.SubjectId,
                UserId = _context.UserId,
                MainAgentKey = request.MainAgentKey,
                Title = request.Title,
                Status = EmConversationStatus.Active,
                CreatedAt = DateTime.Now
            };

            if (!await _store.InsertConversationAsync(conversation)) return VivApiResult.Failed("创建会话失败");

            return VivApiResult.Success(new CreateConversationOutput { ConversationKey = conversation.ConversationKey });
        }

        /// <summary>
        /// 当前主体的会话列表
        /// </summary>
        public async Task<VivApiResult> ListAsync(int pageIndex = 1, int pageSize = 20)
        {
            var rows = await _store.ListConversationsAsync(_context.SubjectId, null, pageIndex, pageSize);
            return VivApiResult.Success(rows.Select(ToItem).ToList());
        }

        /// <summary>
        /// 会话详情与最近若干条消息
        /// </summary>
        public async Task<VivApiResult> GetAsync(Guid conversationKey, int lastMessageCount = 50)
        {
            var conversation = await LoadOwnedAsync(conversationKey);
            if (conversation is null) return VivApiResult.Failed("会话不存在");

            var all = await _store.ListMessagesAsync(conversation.Id);
            var messages = lastMessageCount > 0 && all.Count > lastMessageCount
                ? all.GetRange(all.Count - lastMessageCount, lastMessageCount)
                : all;

            return VivApiResult.Success(new ConversationDetailOutput
            {
                ConversationKey = conversation.ConversationKey,
                MainAgentKey = conversation.MainAgentKey,
                Title = conversation.Title,
                Status = (int)conversation.Status,
                Messages = messages.Select(ToItem).ToList()
            });
        }

        /// <summary>
        /// 增量拉消息（afterSeq 之后）
        /// </summary>
        public async Task<VivApiResult> ListMessagesAsync(Guid conversationKey, int afterSeq = 0, int limit = 200)
        {
            var conversation = await LoadOwnedAsync(conversationKey);
            if (conversation is null) return VivApiResult.Failed("会话不存在");

            var messages = await _store.ListMessagesAsync(conversation.Id, afterSeq, limit);
            return VivApiResult.Success(messages.Select(ToItem).ToList());
        }

        /// <summary>
        /// 结束会话（状态置 2）
        /// </summary>
        public async Task<VivApiResult> CloseAsync(Guid conversationKey)
        {
            var conversation = await LoadOwnedAsync(conversationKey);
            if (conversation is null) return VivApiResult.Failed("会话不存在");

            conversation.Status = EmConversationStatus.Closed;
            return await _store.UpdateConversationAsync(conversation)
                ? VivApiResult.Success()
                : VivApiResult.Failed("结束会话失败");
        }

        /// <summary>
        /// 取会话并校验归属：越权一律当"不存在"处理，不泄露存在性
        /// </summary>
        private async Task<OtConversation?> LoadOwnedAsync(Guid conversationKey)
        {
            var conversation = await _store.GetConversationAsync(conversationKey);
            return conversation is null || conversation.SubjectId != _context.SubjectId ? null : conversation;
        }

        /// <summary>
        /// 会话实体 → 列表项
        /// </summary>
        private static ConversationItemOutput ToItem(OtConversation x) => new()
        {
            ConversationKey = x.ConversationKey,
            MainAgentKey = x.MainAgentKey,
            Title = x.Title,
            Status = (int)x.Status,
            MessageCount = x.MessageCount,
            LastMessageAt = x.LastMessageAt,
            TotalInputTokens = x.TotalInputTokens,
            TotalOutputTokens = x.TotalOutputTokens
        };

        /// <summary>
        /// 消息实体 → 列表项
        /// </summary>
        private static MessageItemOutput ToItem(OtMessage m) => new()
        {
            Seq = m.Seq,
            Role = m.Role,
            Content = m.Content,
            AgentKey = m.AgentKey,
            InputTokens = m.InputTokens,
            OutputTokens = m.OutputTokens,
            CreatedAt = m.CreatedAt
        };
    }
}
