namespace Viv.Herta.Core.IService
{
    /// <summary>
    /// 在线态。跨副本共享，Redis 不可用时返回 null（未知）而不是抛异常。
    /// </summary>
    public interface IPresenceStore
    {
        /// <summary>连接建立：计数 +1 并设置租约</summary>
        Task OnlineAsync(long tenantId, long userId);

        /// <summary>连接断开：计数 -1，减到 0 删除键</summary>
        Task OfflineAsync(long tenantId, long userId);

        /// <summary>心跳续期</summary>
        Task RenewAsync(long tenantId, long userId);

        /// <summary>null = 查询失败，状态未知</summary>
        Task<bool?> IsOnlineAsync(long tenantId, long userId);

        Task<Dictionary<long, bool?>> IsOnlineAsync(long tenantId, IReadOnlyList<long> userIds);
    }
}
