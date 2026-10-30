using System.Data;
using System.Data.Common;

using Dapper;

using Viv.Contracts.Enums;
using Viv.Contracts.Exceptions;
using Viv.Delusion.Generic;
using Viv.Momo;
using Viv.Momo.Core;
using Viv.Momo.Dialect;
using Viv.Momo.Enums;

namespace Viv.Skirk.Core
{
    /// <summary>
    /// 任务表读写。手写 SQL + Dapper，不经过 EF（模块独立、不参与实体扫描）。
    ///
    /// 建表另开一条连接，不并进调用方的业务事务：DDL 跟着业务回滚的话，
    /// 进程里的「表已建好」标记会指着一张并不存在的表。
    /// 认领、取消标记、幂等回查打主库，并带上当前事务句柄。
    /// </summary>
    public sealed class VivAsyncTaskStore : IVivAsyncTaskStore
    {
        private readonly IMomoDbContext _db;

        public VivAsyncTaskStore(IMomoDbContext db)
        {
            _db = db;
        }

        private ISqlDialect Dialect => _db.Dialect;

        #region 建表

        /// <summary>建表 + 建索引（幂等）。走独立连接，提交与业务事务无关。</summary>
        public async Task EnsureTableAsync(CancellationToken cancellationToken = default)
        {
            var template = _db.GetDbConnection(DbReadWriteType.Write);
            if (template is not DbConnection db || string.IsNullOrWhiteSpace(db.ConnectionString))
            {
                throw new InvalidOperationException("写库连接未就绪，无法在事务外建表");
            }

            var created = Activator.CreateInstance(db.GetType(), db.ConnectionString);
            if (created is not DbConnection owned)
            {
                throw new InvalidOperationException($"无法为 {db.GetType().FullName} 另开一条连接来建表");
            }

            await using (owned)
            {
                await owned.OpenAsync(cancellationToken).ConfigureAwait(false);
                foreach (var sql in VivAsyncTaskSql.CreateTable(Dialect))
                {
                    await owned.ExecuteAsync(new CommandDefinition(sql, cancellationToken: cancellationToken))
                        .ConfigureAwait(false);
                }
            }
        }

        #endregion

        #region 入队 / 查询 / 取消

        public Task<VivAsyncTask?> GetAsync(long id, CancellationToken cancellationToken = default)
            => _db.SingleOrDefaultAsync<VivAsyncTask>(
                VivAsyncTaskSql.SelectById(Dialect), new { Id = id }, cancellationToken);

        public Task<long?> FindIdByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
            => QuerySingleOnWriteAsync<long?>(
                VivAsyncTaskSql.SelectByIdempotencyKey(Dialect),
                new { IdempotencyKey = idempotencyKey },
                cancellationToken);

        public Task<bool> InsertAsync(VivAsyncTask task, CancellationToken cancellationToken = default)
            => _db.ExecuteSqlAsync(VivAsyncTaskSql.Insert(Dialect), task, cancellationToken);

        /// <summary>分页查询：先查总数，再取当前页</summary>
        public async Task<PagedList<VivAsyncTask>> QueryAsync(VivAsyncTaskQuery query, CancellationToken cancellationToken = default)
        {
            var pageIndex = Math.Max(1, query.PageIndex);
            var pageSize = Math.Clamp(query.PageSize, 1, 200);

            var parameters = new
            {
                query.SubjectId,
                query.UserId,
                query.TaskType,
                Status = query.Status is null ? (int?)null : (int)query.Status.Value,
                PageSize = pageSize,
                Offset = (pageIndex - 1) * pageSize,
            };

            var total = await _db.FindScalarAsync<long>(VivAsyncTaskSql.QueryCount(Dialect), parameters, cancellationToken)
                .ConfigureAwait(false);

            var items = total == 0
                ? []
                : await _db.FindListAsync<VivAsyncTask>(VivAsyncTaskSql.QueryItems(Dialect), parameters, cancellationToken)
                    .ConfigureAwait(false);

            return new PagedList<VivAsyncTask>(pageIndex, pageSize)
            {
                Items = items,
                TotalCount = total,
                TotalPages = (int)Math.Ceiling(total / (double)pageSize),
                IsHaveFrontPage = pageIndex > 1,
                IsHaveNextPage = pageIndex * (long)pageSize < total,
            };
        }

        public Task<bool> RequestCancelAsync(long id, CancellationToken cancellationToken = default)
            => _db.ExecuteSqlAsync(
                VivAsyncTaskSql.RequestCancel(Dialect),
                new
                {
                    Id = id,
                    CancelRequested = true,
                    Pending = (int)VivAsyncTaskStatus.Pending,
                    Running = (int)VivAsyncTaskStatus.Running,
                },
                cancellationToken);

        #endregion

        #region Worker

        /// <summary>原子认领最多 <paramref name="batchSize"/> 条任务</summary>
        public async Task<List<VivAsyncTask>> ClaimBatchAsync(
            int batchSize,
            string leaseOwner,
            DateTime now,
            DateTime leaseUntil,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var rows = await QueryOnWriteAsync<VivAsyncTask>(
                    VivAsyncTaskSql.ClaimBatch(Dialect),
                    new
                    {
                        BatchSize = Math.Max(1, batchSize),
                        LeaseOwner = leaseOwner,
                        Now = now,
                        LeaseUntil = leaseUntil,
                        Pending = (int)VivAsyncTaskStatus.Pending,
                        Running = (int)VivAsyncTaskStatus.Running,
                    },
                    cancellationToken).ConfigureAwait(false);

                return rows.AsList();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var connType = Dialect.Source == DatabaseSourceType.PostgreSQL
                    ? VivConnType.PostgreSQL
                    : VivConnType.SqlServer;
                throw new VivConnectionException(connType, "Skirk ClaimBatch", ex);
            }
        }

        /// <summary>续租：返回 false 说明租约已被别人拿走（本副本应放弃该任务）</summary>
        public async Task<bool> RenewLeaseAsync(long id, string leaseOwner, DateTime leaseUntil, CancellationToken cancellationToken = default)
            => await _db.ExecuteSqlAsync(
                VivAsyncTaskSql.RenewLease(Dialect),
                new { Id = id, LeaseOwner = leaseOwner, LeaseUntil = leaseUntil },
                cancellationToken).ConfigureAwait(false);

        /// <summary>回收过期租约。还没到重试上限的回到待执行，已经用尽的标成失败。</summary>
        public Task<bool> ReleaseExpiredLeasesAsync(DateTime now, DateTime expireAt, CancellationToken cancellationToken = default)
            => _db.ExecuteSqlAsync(
                VivAsyncTaskSql.ReleaseExpiredLeases(Dialect),
                new
                {
                    Now = now,
                    ExpireAt = expireAt,
                    Pending = (int)VivAsyncTaskStatus.Pending,
                    Running = (int)VivAsyncTaskStatus.Running,
                    Failed = (int)VivAsyncTaskStatus.Failed,
                    ErrorMessage = "执行方崩溃且已达到重试上限",
                },
                cancellationToken);

        public Task<bool> CompleteAsync(
            long id,
            string leaseOwner,
            string? resultJson,
            DateTime now,
            DateTime expireAt,
            CancellationToken cancellationToken = default)
            => _db.ExecuteSqlAsync(
                VivAsyncTaskSql.Complete(Dialect),
                new
                {
                    Id = id,
                    LeaseOwner = leaseOwner,
                    ResultJson = resultJson,
                    Now = now,
                    ExpireAt = expireAt,
                    Succeeded = (int)VivAsyncTaskStatus.Succeeded,
                },
                cancellationToken);

        /// <summary>失败：<paramref name="status"/> 为 Pending 时是"排队重试"，Failed 时是"终态失败"</summary>
        public Task<bool> FailAsync(
            long id,
            string leaseOwner,
            VivAsyncTaskStatus status,
            string? errorMessage,
            DateTime? nextAttemptAt,
            DateTime now,
            DateTime? expireAt,
            CancellationToken cancellationToken = default)
            => _db.ExecuteSqlAsync(
                VivAsyncTaskSql.Fail(Dialect),
                new
                {
                    Id = id,
                    LeaseOwner = leaseOwner,
                    Status = (int)status,
                    ErrorMessage = errorMessage,
                    NextAttemptAt = nextAttemptAt ?? now,
                    Now = now,
                    ExpireAt = expireAt,
                    Failed = (int)VivAsyncTaskStatus.Failed,
                },
                cancellationToken);

        public Task<bool> MarkCanceledAsync(long id, DateTime now, DateTime expireAt, CancellationToken cancellationToken = default)
            => _db.ExecuteSqlAsync(
                VivAsyncTaskSql.MarkCanceled(Dialect),
                new { Id = id, Now = now, ExpireAt = expireAt, Canceled = (int)VivAsyncTaskStatus.Canceled },
                cancellationToken);

        public Task<bool> ReportProgressAsync(long id, int progress, int? totalCount, int? doneCount, string? message, CancellationToken cancellationToken = default)
            => _db.ExecuteSqlAsync(
                VivAsyncTaskSql.ReportProgress(Dialect),
                new { Id = id, Progress = progress, TotalCount = totalCount, DoneCount = doneCount, ProgressMessage = message },
                cancellationToken);

        public Task<bool?> IsCancelRequestedAsync(long id, CancellationToken cancellationToken = default)
            => QuerySingleOnWriteAsync<bool?>(VivAsyncTaskSql.IsCancelRequested(Dialect), new { Id = id }, cancellationToken);

        /// <summary>主库上的查询。带上当前事务，避免打到从库，也避免事务内命令被拒绝。</summary>
        private async Task<T?> QuerySingleOnWriteAsync<T>(string sql, object parameters, CancellationToken cancellationToken)
        {
            var connection = _db.GetDbConnection(DbReadWriteType.Write);
            return await connection.QuerySingleOrDefaultAsync<T>(
                new CommandDefinition(sql, parameters, AmbientTransaction, cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }

        private async Task<IEnumerable<T>> QueryOnWriteAsync<T>(string sql, object parameters, CancellationToken cancellationToken)
        {
            var connection = _db.GetDbConnection(DbReadWriteType.Write);
            return await connection.QueryAsync<T>(
                new CommandDefinition(sql, parameters, AmbientTransaction, cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }

        private IDbTransaction? AmbientTransaction => (_db as MomoDatabase)?.AmbientTransaction;

        /// <summary>清理保留期已过的终态任务</summary>
        public Task<bool> CleanupAsync(int batchSize, DateTime now, CancellationToken cancellationToken = default)
            => _db.ExecuteSqlAsync(
                VivAsyncTaskSql.Cleanup(Dialect),
                new { BatchSize = Math.Max(1, batchSize), Now = now },
                cancellationToken);

        #endregion
    }
}
