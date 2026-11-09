namespace Viv.Kinna.Options
{
    /// <summary>长连接层的配置。时间粒度到秒的用 int（配合配置中心的写法），需要亚秒精度的用 TimeSpan。新增项给默认值，不配也能启动。</summary>
    public sealed class KinnaOptions
    {
        /// <summary>
        /// Hub 的映射路径。
        /// </summary>
        public string HubPath { get; set; } = "/kinna";

        /// <summary>
        /// 是否把异常细节发给客户端，生产环境要关。
        /// </summary>
        public bool EnableDetailedErrors { get; set; }

        /// <summary>
        /// 用户 Key 和群名的前缀。组名只在进程内有效，改前缀不影响已经连上的客户端；
        /// 但一个进程里装两个 Hub 时必须不同，否则会串组。
        /// </summary>
        public string Prefix { get; set; } = "kinna";

        /// <summary>
        /// 连接租约时长，超过这么久没收到 Ping 的连接会被清掉。
        /// </summary>
        public int EntryTtlSeconds { get; set; } = 90;

        /// <summary>
        /// 租约清扫间隔。
        /// </summary>
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

        /// <summary>
        /// 上行配额的窗口长度，配合 <see cref="UplinkPermitLimit"/> 使用。
        /// </summary>
        public int UplinkWindowSeconds { get; set; } = 10;

        /// <summary>
        /// 是否注册连接数健康检查。
        /// </summary>
        public bool EnableHealthCheck { get; set; } = true;

        /// <summary>
        /// 同一个客户端（租户 + 用户 + AppId）只保留一条连接。新连接进来时踢掉旧的，
        /// 避免重装、重连在连接表里留下幽灵连接。只在本副本内生效——旧连接在别的副本上
        /// 时需要广播踢人，暂不支持。配 false 关闭。
        /// </summary>
        public bool SingleConnectionPerApp { get; set; } = true;

        /// <summary>
        /// 原生 WebSocket 的挂载路径。空串表示不启用，只跑 SignalR。
        ///
        /// 这里配的是**下游**路径，不要带网关的 <c>/ws</c> 前缀：网关把
        /// <c>/ws/{短名}/**</c> 改写成 <c>/**</c>，客户端连 <c>/ws/hertalink/chat-ws</c>，
        /// 到达本进程的就是 <c>/chat-ws</c>。网关只在 <c>/ws</c> 前缀下从 query string 取
        /// access_token（SignalR 的 <c>/hubs/*</c> 走自己的约定）。
        /// </summary>
        public string WebSocketPath { get; set; } = string.Empty;

        /// <summary>
        /// 单条原生连接待发队列的帧数上限。填满说明客户端跟不上，直接断开让客户端重连补拉，
        /// 继续排队只会把服务端内存吃掉。
        /// </summary>
        public int WebSocketQueueCapacity { get; set; } = 512;

        /// <summary>
        /// 上行单帧的字节上限。超了直接拒帧。上行只有 Ping/Ack/Resync 三种指令，
        /// 正常不会超过几百字节，给到 32KB 是留余量。
        /// </summary>
        public int WebSocketMaxMessageBytes { get; set; } = 32 * 1024;

        /// <summary>
        /// Kestrel 主动发 WebSocket ping 帧的间隔。0 表示用宿主默认值。
        /// 这个 ping 是协议层控制帧，与业务的心跳不同：它负责发现 TCP 半开，
        /// 业务心跳负责续租。
        ///
        /// `UseWebSockets` 是进程级的，多 Hub 时只能有一个值：第二个 Hub 配了不同的值会在启动时直接报错，
        /// 不静默取第一个。
        /// </summary>
        public int WebSocketKeepAliveSeconds { get; set; } = 30;

        /// <summary>
        /// 等关闭帧真正写出去的上限。超了说明写循环已死或客户端根本不读，调用方直接硬断。
        /// 只有在「关闭是这次收尾的主因」时才等它，软踢/停机走各自的宽限。
        /// </summary>
        public TimeSpan WebSocketCloseTimeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// 软踢之后等写循环排空的宽限，到点硬断。必须短于 <see cref="WebSocketCloseTimeout"/>。
        /// </summary>
        public TimeSpan WebSocketTerminateGrace { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// 停机时给每条连接补发 1012 关闭帧的等待上限。停机是同步回调，给得比软踢更短：
        /// 目标是「让客户端知道原因」，不是「保证送达」。
        ///
        /// 200ms 在冷启动/线程池有压力时连一帧都写不完，实测会偶发拿不到 1012，
        /// 所以给到 500ms；仍远短于 <see cref="WebSocketCloseTimeout"/>。
        /// </summary>
        public TimeSpan WebSocketShutdownGrace { get; set; } = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// 浅拷贝一份。DI 里注册的是「共享配置」单例，每个 Hub 必须在这份副本上做自己的覆盖，
        /// 否则后注册的 Hub 会把先注册的 HubPath / Prefix 改掉，两个 Hub 还指向同一个对象。
        /// 属性全是值类型和 string，浅拷贝等同于深拷贝；以后加引用类型字段要改成显式复制。
        /// </summary>
        public KinnaOptions Clone() => (KinnaOptions)MemberwiseClone();
    }
}
