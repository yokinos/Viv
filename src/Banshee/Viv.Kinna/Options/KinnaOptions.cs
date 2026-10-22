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

        /// <summary>
        /// 每连接的上行配额：窗口内允许几次 Ping、Ack。配 0 表示不限流。
        /// 限流只挡高频请求，不惩罚客户端——被挡下的 Ack 下次照常上报。
        /// </summary>
        public int UplinkPermitLimit { get; set; } = 20;

        /// <summary>上行配额的窗口长度，配合 <see cref="UplinkPermitLimit"/> 使用。</summary>
        public int UplinkWindowSeconds { get; set; } = 10;

        /// <summary>是否注册连接数健康检查。</summary>
        public bool EnableHealthCheck { get; set; } = true;
    }
}
