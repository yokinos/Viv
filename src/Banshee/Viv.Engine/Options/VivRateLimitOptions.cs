namespace Viv.Engine.Options
{
    /// <summary>
    /// 网关限流配置。当前实现是「按客户端 IP 分区的固定窗口」，所以只有下面三项生效；
    /// 曾经预留的分段/令牌桶参数（ReplenishmentPeriod、SegmentsPerWindow、TokenLimit 等）
    /// 从未被消费，已删除 —— 想换算法时再加，不要留一堆改了不起作用的旋钮。
    /// </summary>
    public class VivRateLimitOptions
    {
        /// <summary>
        /// viv.ratelimit.json 中限流配置的节名
        /// </summary>
        public const string CustomRateLimit = "CustomRateLimit";

        /// <summary>每个客户端在窗口内允许的请求数。<c>&lt;= 0</c> 表示不限流。</summary>
        public int PermitLimit { get; set; } = 100;

        /// <summary>窗口长度（秒），配置为 0 或负数时按 1 秒处理。</summary>
        public int Window { get; set; } = 10;

        /// <summary>超出配额后的排队长度，0 表示不排队、直接拒绝。</summary>
        public int QueueLimit { get; set; } = 2;
    }
}
