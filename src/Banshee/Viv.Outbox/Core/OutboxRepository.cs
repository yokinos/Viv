using System.Data;
using Dapper;
using Viv.Contracts.Enums;
using Viv.Contracts.Exceptions;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Enums;
using Viv.Momo.Interface;

namespace Viv.Outbox.Core
{
    /// <summary>
    /// <see cref="IOutboxRepository"/> 的手写 SQL 实现。
    ///
    /// 两个执行通道：写操作（入队 / 改状态 / 清理）一律走 <c>IMomoDbContext.ExecuteSqlAsync</c>，
    /// 它取写库上下文并把 <c>_transaction</c> 转发给 Dapper，入队因此自然并入调用方的业务事务。
    ///
    /// 认领是唯一的例外：它要用 <c>UPDATE ... OUTPUT/RETURNING</c> 把行拿回来，而 IMomoDbContext 上
    /// 所有返回行的原生 SQL 方法走的全是读库上下文，拿来跑认领会打到从库上。所以这里用
    /// <c>GetDbConnection(DbReadWriteType.Write)</c> 取主库连接自己跑 Dapper ——
    /// 认领是单条语句、自身即原子，不需要事务参数。
    /// </summary>
    internal sealed class OutboxRepository : IOutboxRepository
    {
        private const int MaxErrorLength = 2000;

        private readonly IMomoDbContext _db;
        private readonly ILoggerContract _logger;
        private readonly DatabaseSourceType _source;
        private readonly int _timeout;

        public OutboxRepository(IMomoDbContext db, IDatabaseOptionsProvider optionsProvider, ILoggerContract logger)
        {
            _db = db;
            _logger = logger;

            var options = optionsProvider.GetRealOptions();
            _source = options.DatabaseSource;
            _timeout = options.Timeout;
        }

        public async Task EnsureTableAsync(CancellationToken cancellationToken = default)
        {
            // DDL 是幂等的，多实例并发启动安全；失败会由 ExecuteSqlAsync 包成连接异常抛出，
            // 不吞 —— 表都建不出来，投递器继续跑只会反复报同一个错。
            await _db.ExecuteSqlAsync(OutboxSql.CreateTable(_source)).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        public Task<bool> InsertAsync(OutboxMessage message, CancellationToken cancellationToken = default)
            => _db.ExecuteSqlAsync(OutboxSql.Insert, new
            {
                message.Id,
                message.MessageId,
                message.EventType,
                message.Payload,
                Status = (int)message.Status,
                message.RetryCount,
                message.NextRetryAt,
                message.OccurredAt,
            });

        public async Task ReleaseExpiredLeasesAsync(DateTime now, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _db.ExecuteSqlAsync(OutboxSql.ReleaseExpiredLeases, new { Now = now }).ConfigureAwait(false);
        }

        public async Task<List<OutboxMessage>> ClaimBatchAsync(
            int batchSize, DateTime now, DateTime leaseUntil, CancellationToken cancellationToken = default)
        {
            var sql = OutboxSql.ClaimBatch(_source);
            var parameters = new { BatchSize = batchSize, Now = now, LeaseUntil = leaseUntil };

            try
            {
                // 主库连接。Dapper 会自己处理连接的开/关状态。
                var connection = _db.GetDbConnection(DbReadWriteType.Write);
                var rows = await connection.QueryAsync<OutboxMessage>(
                    new CommandDefinition(
                        sql,
                        parameters,
                        transaction: null,
                        commandTimeout: _timeout,
                        commandType: CommandType.Text,
                        cancellationToken: cancellationToken)).ConfigureAwait(false);

                return rows.AsList();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 这条没经过 Momo，得自己包成同一个异常类型，投递器与业务才看到一致的错误面
                _logger.Error($"发件箱认领失败（{_source}）", ex);
                var connType = _source == DatabaseSourceType.PostgreSQL
                    ? VivConnType.PostgreSQL
                    : VivConnType.SqlServer;
                throw new VivConnectionException(connType, "Outbox ClaimBatch", ex);
            }
        }

        public async Task MarkSentAsync(long id, DateTime now, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _db.ExecuteSqlAsync(OutboxSql.MarkSent, new { Id = id, Now = now }).ConfigureAwait(false);
        }

        public async Task MarkPendingAsync(
            long id, int retryCount, DateTime nextRetryAt, string? lastError, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _db.ExecuteSqlAsync(OutboxSql.MarkPending, new
            {
                Id = id,
                RetryCount = retryCount,
                NextRetryAt = nextRetryAt,
                LastError = Truncate(lastError),
            }).ConfigureAwait(false);
        }

        public async Task MarkFailedAsync(
            long id, int retryCount, string? lastError, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _db.ExecuteSqlAsync(OutboxSql.MarkFailed, new
            {
                Id = id,
                RetryCount = retryCount,
                LastError = Truncate(lastError),
            }).ConfigureAwait(false);
        }

        public Task<bool> CleanupBatchAsync(DateTime cutoff, int batchSize, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _db.ExecuteSqlAsync(OutboxSql.CleanupBatch(_source), new { Cutoff = cutoff, BatchSize = batchSize });
        }

        /// <summary>LastError 列宽 2000，超长直接截断 —— 宁可少几个字，也别让整条标记语句失败。</summary>
        private static string? Truncate(string? error)
            => error is null || error.Length <= MaxErrorLength ? error : error[..MaxErrorLength];
    }
}
