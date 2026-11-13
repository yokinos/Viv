namespace Viv.Engine.RateLimit
{
    /// <summary>
    /// 限流计数器的存储位置。抽出来是为了让中间件不直接绑 Redis ——
    /// Redis 在网关里是可选件（只有选了它做缓存提供者才有 <c>IRedisService</c>），
    /// 而限流必须在「有 Redis」和「没 Redis」两种部署下都能工作。
    /// </summary>
    internal interface IRateLimitCounter
    {
        /// <summary>
        /// 给窗口计数 +1 并返回自增后的值。
        /// 返回 <c>null</c> 表示计数器当前不可用（没配 Redis、连不上、超时）——
        /// 这种情况由调用方决定降级策略，不要在这里抛：
        /// 限流器故障不该让整个网关变成 500。
        /// </summary>
        /// <param name="key">窗口键，已包含客户端标识与时间片</param>
        /// <param name="ttl">键的存活时间，须覆盖整个窗口</param>
        Task<long?> TryIncrementAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default);
    }
}
