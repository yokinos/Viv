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

        /// <summary>
        /// 把没跑完的任务还回队列：状态回 Pending、清租约、<c>Attempt - 1</c>、<c>NextAttemptAt = now</c>。
        ///
        /// 与 <see cref="FailAsync"/> 的区别是「这次到底有没有失败」：被外部叫停不是失败，
        /// 按失败记账会让一次发布吃掉一整轮重试（认领时 Attempt 已经 +1）。
        /// 只对仍由 <paramref name="leaseOwner"/> 持有的行生效。
        /// </summary>
        Task<bool> ReleaseAsync(long id, string leaseOwner, DateTime now, CancellationToken cancellationToken = default);

        Task<bool> FailAsync(
            long id,
            string leaseOwner,
            VivAsyncTaskStatus status,
            string? errorMessage,
            DateTime? nextAttemptAt,
            DateTime now,
            DateTime? expireAt,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 标记取消。只对仍由 <paramref name="leaseOwner"/> 持有的行生效。
        ///
        /// 执行方是「轮询到 CancelRequested → 中断本地执行 → 写回 Canceled」，中间隔着一次网络往返：
        /// 租约可能正好在这个窗口里过期并被别的副本接手。不带租约条件的话，旧副本会把新持有者
        /// 正在跑的行改成 Canceled 并清掉它的租约，新副本执行完之后 <see cref="CompleteAsync"/> 必然写不回去，
        /// 副作用已经发生而结果永久丢失。返回 false 表示这次写回没落到自己持有过的行上。
        /// </summary>
        Task<bool> MarkCanceledAsync(long id, string leaseOwner, DateTime now, DateTime expireAt, CancellationToken cancellationToken = default);

        Task<bool> ReportProgressAsync(long id, int progress, int? totalCount, int? doneCount, string? message, CancellationToken cancellationToken = default);

        Task<bool?> IsCancelRequestedAsync(long id, CancellationToken cancellationToken = default);

        Task<bool> CleanupAsync(int batchSize, DateTime now, CancellationToken cancellationToken = default);
    }
}
