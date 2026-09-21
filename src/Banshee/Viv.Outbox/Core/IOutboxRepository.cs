namespace Viv.Outbox.Core
{
    /// <summary>
    /// 发件箱表的读写口
    /// </summary>
    internal interface IOutboxRepository
    {
        /// <summary>
        /// 建表（幂等）。<see cref="Options.OutboxOptions.AutoCreateTable"/>  开时由投递器启动跑一次。
        /// </summary>
        Task EnsureTableAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 入队。经 <c>ExecuteSqlAsync</c> 执行，因而自动并入调用方当前的业务事务。
        /// </summary>
        Task<bool> InsertAsync(OutboxMessage message, CancellationToken cancellationToken = default);

        /// <summary>
        /// 把租约过期的 Processing 行退回 Pending（崩溃恢复）。
        /// </summary>
        Task ReleaseExpiredLeasesAsync(DateTime now, CancellationToken cancellationToken = default);

        /// <summary>
        /// 原子认领一批待投递行并加租约。必须打主库。
        /// </summary>
        Task<List<OutboxMessage>> ClaimBatchAsync(int batchSize, DateTime now, DateTime leaseUntil, CancellationToken cancellationToken = default);

        /// <summary>
        /// 投递成功。
        /// </summary>
        Task MarkSentAsync(long id, DateTime now, CancellationToken cancellationToken = default);

        /// <summary>
        /// 投递失败但还要重试：退回 Pending 并推后 <c>NextRetryAt</c>。
        /// </summary>
        Task MarkPendingAsync(long id, int retryCount, DateTime nextRetryAt, string? lastError, CancellationToken cancellationToken = default);

        /// <summary>
        /// 重试耗尽，置为 Failed 等人工介入。
        /// </summary>
        Task MarkFailedAsync(long id, int retryCount, string? lastError, CancellationToken cancellationToken = default);

        /// <summary>
        /// 删一批已发送且超保留期的行。
        /// <returns>本批是否删掉了东西（false = 已清干净，调用方停止循环）。</returns>
        /// </summary>
        Task<bool> CleanupBatchAsync(DateTime cutoff, int batchSize, CancellationToken cancellationToken = default);

        /// <summary>Pending / Failed 条数，给运行指标用。</summary>
        Task<(long Pending, long Failed)> CountDepthAsync(CancellationToken cancellationToken = default);
    }
}
