using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Viv.Contracts.Interface;
using Viv.Entity.Database.Ouroboros;
using Viv.Log;
using Viv.Momo;
using Viv.Ouroboros.Core.IService;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// IAgentStore 实现。全部走 IMomoDbContext，不加缓存 ——
    /// 会话与消息是强一致诉求，缓存留给上层（档位、Agent 装配）。
    /// </summary>
    public class AgentStore : IAgentStore, IDependency
    {
        private readonly IMomoDbContext _dbContext;
        private readonly ILoggerContract _logger;

        public AgentStore(IMomoDbContext dbContext, ILoggerContract logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        #region 会话

        public async Task<OtConversation?> GetConversationAsync(Guid conversationKey)
            => await _dbContext.SingleOrDefaultAsync<OtConversation>(x => x.ConversationKey == conversationKey);

        public async Task<List<OtConversation>> ListConversationsAsync(long? subjectId, long? userId, int pageIndex, int pageSize)
        {
            var rows = await _dbContext.FindListAsync<OtConversation>(x => x.SubjectId == subjectId && (userId == null || x.UserId == userId));

            return rows.OrderByDescending(x => x.LastMessageAt ?? x.CreatedAt)
                       .Skip(Math.Max(0, (pageIndex - 1) * pageSize))
                       .Take(pageSize <= 0 ? 20 : pageSize)
                       .ToList();
        }

        public async Task<bool> InsertConversationAsync(OtConversation conversation)
        {
            conversation.CreatedAt = DateTime.Now;
            return await _dbContext.InsertAsync(conversation);
        }

        public async Task<bool> UpdateConversationAsync(OtConversation conversation)
        {
            conversation.UpdatedAt = DateTime.Now;
            return await _dbContext.UpdateAsync(conversation);
        }

        #endregion

        #region 消息

        public async Task<List<OtMessage>> ListMessagesAsync(long conversationId, int afterSeq = 0, int limit = 0)
        {
            var rows = await _dbContext.FindListAsync<OtMessage>(x => x.ConversationId == conversationId && x.Seq > afterSeq);
            var ordered = rows.OrderBy(x => x.Seq);

            return limit > 0 ? ordered.Take(limit).ToList() : ordered.ToList();
        }

        public async Task<int> NextSeqAsync(long conversationId)
        {
            var rows = await _dbContext.FindListAsync<OtMessage>(x => x.ConversationId == conversationId);
            return rows.Count == 0 ? 1 : rows.Max(x => x.Seq) + 1;
        }

        public async Task<bool> InsertMessageAsync(OtMessage message)
        {
            message.CreatedAt = DateTime.Now;
            return await _dbContext.InsertAsync(message);
        }

        #endregion

        #region 会话状态快照

        public async Task<OtAgentSessionState?> GetSessionStateAsync(long conversationId, string agentKey)
            => await _dbContext.SingleOrDefaultAsync<OtAgentSessionState>(x => x.ConversationId == conversationId && x.AgentKey == agentKey);

        public async Task<bool> SaveSessionStateAsync(long conversationId, string agentKey, string stateJson)
        {
            var existing = await GetSessionStateAsync(conversationId, agentKey);

            if (existing is null)
            {
                return await _dbContext.InsertAsync(new OtAgentSessionState
                {
                    ConversationId = conversationId,
                    AgentKey = agentKey,
                    StateJson = stateJson,
                    SchemaVersion = 1,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                });
            }

            existing.StateJson = stateJson;
            existing.UpdatedAt = DateTime.Now;
            return await _dbContext.UpdateAsync(existing);
        }

        #endregion

        #region 审批

        public async Task<bool> InsertApprovalAsync(OtApproval approval)
        {
            approval.CreatedAt = DateTime.Now;
            return await _dbContext.InsertAsync(approval);
        }

        public async Task<OtApproval?> GetApprovalAsync(Guid approvalId)
            => await _dbContext.SingleOrDefaultAsync<OtApproval>(x => x.ApprovalId == approvalId);

        public async Task<List<OtApproval>> ListPendingApprovalsAsync(long? subjectId)
        {
            var rows = await _dbContext.FindListAsync<OtApproval>(x => x.SubjectId == subjectId && x.Status == 1);
            return rows.OrderBy(x => x.RequestedAt).ToList();
        }

        public async Task<bool> UpdateApprovalAsync(OtApproval approval)
        {
            approval.UpdatedAt = DateTime.Now;
            return await _dbContext.UpdateAsync(approval);
        }

        #endregion
    }
}
