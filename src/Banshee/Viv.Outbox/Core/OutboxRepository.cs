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
    /// <see cref="IOutboxRepository"/> 的手写SQL实现。
    /// <list type="bullet">
    /// <item><description>写操作通道：入队、状态更新、清理均使用 <c>IMomoDbContext.ExecuteSqlAsync</c>，内部使用写库上下文并转发 <c>_transaction</c> 至Dapper，消息入队自动并入调用方业务事务。</description></item>
    /// <item><description>认领逻辑特例：认领需要执行 UPDATE ... OUTPUT/RETURNING 获取记录；IMomoDbContext 所有返回数据的原生SQL方法默认走读库，无法满足要求。因此通过 <c>GetDbConnection(DbReadWriteType.Write)</c> 获取主库连接直接使用Dapper执行。认领为单条原子语句，无需传入事务。</description></item>
    /// </list>
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

        public async Task<(long Pending, long Failed)> CountDepthAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var connection = _db.GetDbConnection(DbReadWriteType.Write);
                var row = await connection.QuerySingleAsync<OutboxDepth>(
                    new CommandDefinition(
                        OutboxSql.CountDepth,
                        transaction: null,
                        commandTimeout: _timeout,
                        commandType: CommandType.Text,
                        cancellationToken: cancellationToken)).ConfigureAwait(false);
                return (row.Pending, row.Failed);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error($"发件箱深度统计失败（{_source}）", ex);
                return (0, 0);
            }
        }

        /// <summary>LastError 列宽 2000，超长直接截断 —— 宁可少几个字，也别让整条标记语句失败。</summary>
        private static string? Truncate(string? error)
            => error is null || error.Length <= MaxErrorLength ? error : error[..MaxErrorLength];
    }
}
