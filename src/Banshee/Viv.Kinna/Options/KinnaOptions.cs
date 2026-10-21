namespace Viv.Kinna.Options
{
    /// <summary>长连接层的配置。时间都用秒，新增项给默认值，不配也能启动。</summary>
    public sealed class KinnaOptions
    {
        /// <summary>Hub 的映射路径。</summary>
        public string HubPath { get; set; } = "/kinna";

        /// <summary>是否把异常细节发给客户端，生产环境要关。</summary>
        public bool EnableDetailedErrors { get; set; }

        /// <summary>
        /// 用户 Key 和群名的前缀。组名只在进程内有效，改前缀不影响已经连上的客户端；
        /// 但一个进程里装两个 Hub 时必须不同，否则会串组。
        /// </summary>
        public string Prefix { get; set; } = "kinna";

        /// <summary>连接租约时长，超过这么久没收到 Ping 的连接会被清掉。</summary>
        public int EntryTtlSeconds { get; set; } = 90;

        /// <summary>租约清扫间隔。</summary>
        public int SweepIntervalSeconds { get; set; } = 60;

        /// <summary>
        /// 投递确认最多逐个记多少条消息 Id。超出后只留水位，等于「全部标记已收到」；
        /// 配 0 表示从不记 Id，只留水位。存储实现按这个数决定要不要清理。
        /// </summary>
        public int DeliveryIdLimit { get; set; } = 200;
    }
}
