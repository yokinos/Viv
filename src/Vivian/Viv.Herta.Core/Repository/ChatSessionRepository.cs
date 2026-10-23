using Viv.Contracts;
using Viv.Contracts.Interface;
using Viv.Entity.Database.Herta;
using Viv.Entity.Enums;
using Viv.Herta.Core.IRepository;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Herta.Core.Repository
{
    /// <summary>会话仓储。会话按「每用户一份」建（同一个群，每个成员各有一行）。</summary>
    public class ChatSessionRepository : DataAccessBase, IChatSessionRepository
    {
        /// <summary>EtChatSession.ChatSessionType：0=私聊 1=群聊</summary>
        private const int DirectSession = 0;
        private const int GroupSession = 1;

        private readonly IDistributedLock? _lock;
        private readonly IDatabaseOptionsProvider _databaseOptions;

        public ChatSessionRepository(
            IVivContext context,
            IMomoDbContext dbContext,
            ILoggerContract logger,
            IDatabaseOptionsProvider databaseOptions,
            IDistributedLock? distributedLock = null)
            : base(context, dbContext, logger)
        {
            _databaseOptions = databaseOptions;
            _lock = distributedLock;
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

            if (_lock == null)
                return await InsertAsync(tenantId, userId, receiverType, targetId).ConfigureAwait(false);

            // 两个人同时给对方发首条消息时，各自「查不到 → 插入」会插出重复会话行。
            // 用分布锁把这一段夹住；键按定位列拼，同一对用户/同一个群才会互斥。
            var key = LockKeyMagic.Join("chat:session", tenantId, userId, (long)receiverType, targetId);

            try
            {
                return await _lock.AcquireLockWithExecuteAsync(
                    key,
                    TimeSpan.FromSeconds(5),
                    // 拿到锁再查一次：排在前面的那个请求可能已经建好了
                    async () => await FindAsync(tenantId, userId, receiverType, targetId).ConfigureAwait(false)
                                ?? await InsertAsync(tenantId, userId, receiverType, targetId).ConfigureAwait(false),
                    // 取不到锁就照旧插：极小概率多一行会话，也比发不出消息强
                    async () => await InsertAsync(tenantId, userId, receiverType, targetId).ConfigureAwait(false))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning($"会话创建取锁失败，退化为直接插入: {ex.Message}");
                return await InsertAsync(tenantId, userId, receiverType, targetId).ConfigureAwait(false);
            }
        }

        private async Task<EtChatSession> InsertAsync(long tenantId, long userId, EmChatReceiverType receiverType, long targetId)
        {
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

            try
            {
                await _dbContext.InsertAsync(session).ConfigureAwait(false);
                return session;
            }
            catch (Exception ex)
            {
                // 唯一索引兜底：冲突说明别人先插好了，把那一行查回来用。
                // 只有取锁失败才会走到这里，属罕见路径。
                // SQL Server 的唯一冲突不会打断事务，可以接着查；PostgreSQL 会终止整个事务，
                // 那时下面的查询也会失败，于是把原始异常抛出去 —— 行为退化成「报错」而不是静默重复行。
                _logger.Warning($"会话插入冲突，尝试复用已有行: tenant={tenantId} user={userId} target={targetId} {ex.Message}");

                EtChatSession? existing = null;
                try
                {
                    existing = await FindAsync(tenantId, userId, receiverType, targetId).ConfigureAwait(false);
                }
                catch
                {
                    // 查不动就让原始异常冒出去，别用次要异常盖掉它
                }

                if (existing != null) return existing;
                throw;
            }
        }

        public Task<List<EtChatSession>> FindDirectPairAsync(long tenantId, long userA, long userB)
        {
            // A→B 与 B→A 两行一次查回；条件用 OR 写，EF 翻译成一条 SQL
            return _dbContext.FindListAsync<EtChatSession>(x =>
                x.TenantId == tenantId
                && x.ChatSessionType == DirectSession
                && ((x.UserId == userA && x.TargetUserId == userB)
                    || (x.UserId == userB && x.TargetUserId == userA)));
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

        public async Task TouchManyAsync(IReadOnlyList<long> sessionIds, DateTime lastMessageTime)
        {
            if (sessionIds.Count == 0) return;

            // 手写 SQL 必须走 SqlMagic：PG 下表名列名是 snake_case，写死类名只在 SQL Server 上对
            var source = _databaseOptions.GetRealOptions().DatabaseSource;
            string Col(string name) => SqlMagic.QuoteIdentifier(name, source);
            var table = SqlMagic.GetTableName<EtChatSession>(source);

            if (sessionIds.Count == 1)
            {
                await _dbContext.ExecuteSqlAsync(
                    $"UPDATE {table} SET {Col("LastMessageTime")} = @At WHERE {Col("Id")} = @Id0",
                    new { At = lastMessageTime, Id0 = sessionIds[0] }).ConfigureAwait(false);
                return;
            }

            if (sessionIds.Count == 2)
            {
                // 一条消息最多牵两行（单聊双向），写死两个参数比拼 IN 列表省事
                await _dbContext.ExecuteSqlAsync(
                    $"UPDATE {table} SET {Col("LastMessageTime")} = @At WHERE {Col("Id")} = @Id0 OR {Col("Id")} = @Id1",
                    new { At = lastMessageTime, Id0 = sessionIds[0], Id1 = sessionIds[1] }).ConfigureAwait(false);
                return;
            }

            foreach (var id in sessionIds)
            {
                await TouchAsync(id, lastMessageTime).ConfigureAwait(false);
            }
        }
    }
}
