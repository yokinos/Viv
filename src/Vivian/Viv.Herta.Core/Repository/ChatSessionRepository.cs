using Viv.Contracts.Interface;
using Viv.Entity.Database.Herta;
using Viv.Entity.Enums;
using Viv.Herta.Core.IRepository;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Base;

namespace Viv.Herta.Core.Repository
{
    /// <summary>会话仓储。会话按「每用户一份」建（同一个群，每个成员各有一行）。</summary>
    public class ChatSessionRepository : DataAccessBase, IChatSessionRepository
    {
        /// <summary>EtChatSession.ChatSessionType：0=私聊 1=群聊</summary>
        private const int DirectSession = 0;
        private const int GroupSession = 1;

        public ChatSessionRepository(IVivContext context, IMomoDbContext dbContext, ILoggerContract logger)
            : base(context, dbContext, logger)
        {
        }

        public Task<EtChatSession?> FindAsync(long tenantId, long userId, EmChatReceiverType receiverType, long targetId)
        {
            // 单聊与群聊的定位列不同；条件表达式 EF 不翻译，写成两个分支
            return receiverType == EmChatReceiverType.Group
                ? _dbContext.SingleOrDefaultAsync<EtChatSession>(
                    x => x.TenantId == tenantId && x.UserId == userId && x.ChatSessionType == GroupSession && x.GroupChatId == targetId)
                : _dbContext.SingleOrDefaultAsync<EtChatSession>(
                    x => x.TenantId == tenantId && x.UserId == userId && x.ChatSessionType == DirectSession && x.TargetUserId == targetId);
        }

        public async Task<EtChatSession> GetOrCreateAsync(long tenantId, long userId, EmChatReceiverType receiverType, long targetId)
        {
            var existing = await FindAsync(tenantId, userId, receiverType, targetId).ConfigureAwait(false);
            if (existing != null) return existing;

            var isGroup = receiverType == EmChatReceiverType.Group;
            var session = new EtChatSession
            {
                TenantId = tenantId,
                UserId = userId,
                ChatSessionType = isGroup ? GroupSession : DirectSession,
                TargetUserId = isGroup ? null : targetId,
                GroupChatId = isGroup ? targetId : null,
                CreateAt = DateTime.UtcNow
            };

            await _dbContext.InsertAsync(session).ConfigureAwait(false);
            return session;
        }

        public Task<List<EtChatSession>> GetDirectSessionsAsync(long tenantId, long userId)
        {
            return _dbContext.FindListAsync<EtChatSession>(
                x => x.TenantId == tenantId && x.UserId == userId && x.ChatSessionType == DirectSession);
        }

        public async Task SetLastReadAsync(
            long tenantId, long userId, EmChatReceiverType receiverType, long targetId, long lastReadMessageId)
        {
            var session = await FindAsync(tenantId, userId, receiverType, targetId).ConfigureAwait(false);
            if (session == null || lastReadMessageId <= session.LastReadMsgId) return;

            session.LastReadMsgId = lastReadMessageId;
            await _dbContext.UpdateAsync(session).ConfigureAwait(false);
        }

        public async Task TouchAsync(long sessionId, DateTime lastMessageTime)
        {
            var session = await _dbContext.FindAsync<EtChatSession>(sessionId).ConfigureAwait(false);
            if (session == null) return;

            session.LastMessageTime = lastMessageTime;
            await _dbContext.UpdateAsync(session).ConfigureAwait(false);
        }
    }
}
