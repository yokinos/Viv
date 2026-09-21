namespace Viv.Outbox.Core
{
    internal interface IInboxRepository
    {
        Task EnsureTableAsync(CancellationToken cancellationToken = default);

        /// <summary>插入一行。唯一冲突返回 false，其它故障抛连接异常。</summary>
        Task<bool> TryInsertAsync(string serviceName, long messageId, DateTime acceptedAt, CancellationToken cancellationToken = default);
    }
}
