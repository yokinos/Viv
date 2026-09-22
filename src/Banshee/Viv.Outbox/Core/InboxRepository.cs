using Microsoft.Data.SqlClient;
using Npgsql;
using Viv.Contracts.Exceptions;
using Viv.Momo;
using Viv.Momo.Enums;
using Viv.Momo.Interface;

namespace Viv.Outbox.Core
{
    /// <summary>
    /// Inbox 写入走 <c>ExecuteSqlAsync</c>，与业务写同事务。唯一约束冲突解读成「已经处理过」。
    /// </summary>
    internal sealed class InboxRepository : IInboxRepository
    {
        private readonly IMomoDbContext _db;
        private readonly DatabaseSourceType _source;
        private int _tableEnsured;

        public InboxRepository(IMomoDbContext db, IDatabaseOptionsProvider optionsProvider)
        {
            _db = db;
            _source = optionsProvider.GetRealOptions().DatabaseSource;
        }

        public async Task EnsureTableAsync(CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _tableEnsured) == 1) return;
            await _db.ExecuteSqlAsync(InboxSql.CreateTable(_source)).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref _tableEnsured, 1);
        }

        public async Task<bool> TryInsertAsync(
            string serviceName, string idempotentKey, DateTime acceptedAt, CancellationToken cancellationToken = default)
        {
            await EnsureTableAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await _db.ExecuteSqlAsync(InboxSql.Insert, new
                {
                    ServiceName = serviceName,
                    IdempotentKey = idempotentKey,
                    AcceptedAt = acceptedAt,
                }).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsUniqueViolation(ex))
            {
                return false;
            }
        }

        public async Task<bool> CleanupBatchAsync(
            DateTime cutoff, int batchSize, CancellationToken cancellationToken = default)
        {
            // 先起表：清理器是唯一一条会跑到「这个服务从没写过 Inbox」的路径，
            // 表不存在的话 DELETE 直接抛，而且会每小时抛一次
            await EnsureTableAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            return await _db.ExecuteSqlAsync(InboxSql.CleanupBatch(_source), new
            {
                Cutoff = cutoff,
                BatchSize = batchSize,
            }).ConfigureAwait(false);
        }

        private static bool IsUniqueViolation(Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (current is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
                    return true;

                if (current is SqlException sql && (sql.Number == 2627 || sql.Number == 2601))
                    return true;
            }

            return false;
        }
    }
}
