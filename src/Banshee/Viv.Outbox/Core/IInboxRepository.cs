namespace Viv.Outbox.Core
{
    internal interface IInboxRepository
    {
        Task EnsureTableAsync(CancellationToken cancellationToken = default);

        /// <summary>插入一行。唯一冲突返回 false，其它故障抛连接异常。</summary>
        Task<bool> TryInsertAsync(string serviceName, long messageId, DateTime acceptedAt, CancellationToken cancellationToken = default);

        /// <summary>删一批早于 <paramref name="cutoff"/> 的行。返回 false = 这批没删到东西，调用方可以收工。</summary>
        Task<bool> CleanupBatchAsync(DateTime cutoff, int batchSize, CancellationToken cancellationToken = default);
    }
}
