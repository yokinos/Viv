using Viv.Delusion.Generic;

namespace Viv.Skirk.Core
{
    /// <summary>
    /// 任务表读写。入队走业务事务，认领和回查打主库。
    /// </summary>
    public interface IVivAsyncTaskStore
    {
        Task EnsureTableAsync(CancellationToken cancellationToken = default);

        Task<VivAsyncTask?> GetAsync(long id, CancellationToken cancellationToken = default);

        Task<long?> FindIdByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);

        Task<bool> InsertAsync(VivAsyncTask task, CancellationToken cancellationToken = default);

        Task<PagedList<VivAsyncTask>> QueryAsync(VivAsyncTaskQuery query, CancellationToken cancellationToken = default);

        Task<bool> RequestCancelAsync(long id, CancellationToken cancellationToken = default);

        Task<List<VivAsyncTask>> ClaimBatchAsync(
            int batchSize,
            string leaseOwner,
            DateTime now,
            DateTime leaseUntil,
            CancellationToken cancellationToken = default);

        Task<bool> RenewLeaseAsync(long id, string leaseOwner, DateTime leaseUntil, CancellationToken cancellationToken = default);

        /// <summary>
        /// 回收过期租约。还没到重试上限的回到待执行，已经用尽的标成失败。
        /// </summary>
        Task<bool> ReleaseExpiredLeasesAsync(DateTime now, DateTime expireAt, CancellationToken cancellationToken = default);

        Task<bool> CompleteAsync(
            long id,
            string leaseOwner,
            string? resultJson,
            DateTime now,
            DateTime expireAt,
            CancellationToken cancellationToken = default);

        Task<bool> FailAsync(
            long id,
            string leaseOwner,
            VivAsyncTaskStatus status,
            string? errorMessage,
            DateTime? nextAttemptAt,
            DateTime now,
            DateTime? expireAt,
            CancellationToken cancellationToken = default);

        Task<bool> MarkCanceledAsync(long id, DateTime now, DateTime expireAt, CancellationToken cancellationToken = default);

        Task<bool> ReportProgressAsync(long id, int progress, int? totalCount, int? doneCount, string? message, CancellationToken cancellationToken = default);

        Task<bool?> IsCancelRequestedAsync(long id, CancellationToken cancellationToken = default);

        Task<bool> CleanupAsync(int batchSize, DateTime now, CancellationToken cancellationToken = default);
    }
}
