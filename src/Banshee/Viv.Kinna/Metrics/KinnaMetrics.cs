using System.Diagnostics.Metrics;

namespace Viv.Kinna.Metrics
{
    /// <summary>
    /// 长连接层的指标。Meter 名要加进 Viv.Aspire.ServiceDefaults 的 AddMeter 名单才会进 OTLP。
    /// 指标都带 hub 标签，一个进程装多个 Hub 时能分开看。
    /// </summary>
    public static class KinnaMetrics
    {
        public const string MeterName = "Viv.Kinna";

        private static readonly Meter Meter = new(MeterName, "1.0.0");

        /// <summary>本副本活跃连接数，标签 transport 区分 signalr 与 websocket</summary>
        private static readonly UpDownCounter<long> ConnectionCounter = Meter.CreateUpDownCounter<long>("kinna.connections");

        /// <summary>推送次数，标签 kind 由业务自定，result 是 ok/skipped/failed</summary>
        private static readonly Counter<long> DispatchCounter = Meter.CreateCounter<long>("kinna.dispatch");

        /// <summary>事件署名与调用者不一致的次数。模拟发送、机器人代发会出现在这里，不是错误</summary>
        private static readonly Counter<long> IdentityOverrideCounter = Meter.CreateCounter<long>("kinna.identity.override");

        /// <summary>被上行配额挡下的请求，标签 method 是 Ping/Ack/Resync</summary>
        private static readonly Counter<long> UplinkThrottledCounter = Meter.CreateCounter<long>("kinna.uplink.throttled");

        /// <summary>群成员查询或加组失败的次数。连接照常建立，只是这次没进组</summary>
        private static readonly Counter<long> GroupSubscriptionFailedCounter = Meter.CreateCounter<long>("kinna.groups.failed");

        /// <summary>原生 WebSocket 在 Upgrade 之前被拒的次数，标签 reason 见 <see cref="KinnaWebSocketRejectReasons"/></summary>
        private static readonly Counter<long> WebSocketRejectedCounter = Meter.CreateCounter<long>("kinna.ws.rejected");

        /// <summary>原生连接关闭次数，标签 reason 见 <see cref="KinnaWebSocketCloseReasons"/></summary>
        private static readonly Counter<long> WebSocketClosedCounter = Meter.CreateCounter<long>("kinna.ws.closed");

        /// <summary>投递确认落库失败的次数。客户端收到 Failed 会重报</summary>
        private static readonly Counter<long> AckFailedCounter = Meter.CreateCounter<long>("kinna.ack.failed");

        /// <summary>重连对齐读取水位失败的次数。降级成「要求补拉」，连接不拆</summary>
        private static readonly Counter<long> ResyncFailedCounter = Meter.CreateCounter<long>("kinna.resync.failed");

        /// <summary>本副本执行的跨副本踢人次数。长期为 0 说明广播通道没接上或订阅掉了</summary>
        private static readonly Counter<long> RemoteKickCounter = Meter.CreateCounter<long>("kinna.kick.remote");

        /// <summary>广播踢人失败的次数。本机已经踢过，其余副本改由租约过期兜底</summary>
        private static readonly Counter<long> KickBroadcastFailedCounter = Meter.CreateCounter<long>("kinna.kick.broadcast.failed");

        public static void ConnectionAdded(string hub, string transport) =>
            ConnectionCounter.Add(1, HubTag(hub), TransportTag(transport));

        public static void ConnectionRemoved(string hub, string transport) =>
            ConnectionCounter.Add(-1, HubTag(hub), TransportTag(transport));

        public static void RecordDispatch(string hub, string kind, string result) =>
            DispatchCounter.Add(1,
                HubTag(hub),
                new KeyValuePair<string, object?>("kind", kind),
                new KeyValuePair<string, object?>("result", result));

        public static void RecordIdentityOverride(string hub) => IdentityOverrideCounter.Add(1, HubTag(hub));

        public static void RecordUplinkThrottled(string hub, string method) =>
            UplinkThrottledCounter.Add(1, HubTag(hub), new KeyValuePair<string, object?>("method", method));

        public static void RecordGroupSubscriptionFailed(string hub) => GroupSubscriptionFailedCounter.Add(1, HubTag(hub));

        public static void RecordAckFailed(string hub) => AckFailedCounter.Add(1, HubTag(hub));

        public static void RecordResyncFailed(string hub) => ResyncFailedCounter.Add(1, HubTag(hub));

        public static void RecordRemoteKick(string hub) => RemoteKickCounter.Add(1, HubTag(hub));

        public static void RecordKickBroadcastFailed(string hub) => KickBroadcastFailedCounter.Add(1, HubTag(hub));

        public static void RecordWebSocketRejected(string hub, string reason) =>
            WebSocketRejectedCounter.Add(1, HubTag(hub), new KeyValuePair<string, object?>("reason", reason));

        public static void RecordWebSocketClosed(string hub, string reason) =>
            WebSocketClosedCounter.Add(1, HubTag(hub), new KeyValuePair<string, object?>("reason", reason));

        private static KeyValuePair<string, object?> HubTag(string hub) => new("hub", hub);

        private static KeyValuePair<string, object?> TransportTag(string transport) => new("transport", transport);
    }

    /// <summary>原生连接在建立前被拒的原因。值进指标标签，别随手改，改了历史面板就对不上。</summary>
    public static class KinnaWebSocketRejectReasons
    {
        /// <summary>请求不是 WebSocket 升级</summary>
        public const string NotUpgrade = "not_upgrade";

        /// <summary>没注册身份解析器，等于没有鉴权，一律拒绝</summary>
        public const string NoResolver = "no_resolver";

        /// <summary>准入判定不通过</summary>
        public const string Admission = "admission";

        /// <summary>身份不完整（准入放行但解析结果为空）</summary>
        public const string NoIdentity = "no_identity";

        /// <summary>登记失败（身份缺字段）</summary>
        public const string RegisterFailed = "register_failed";

        /// <summary>声明了子协议但一个都不认识（多半是客户端版本比服务端新）</summary>
        public const string SubProtocol = "subprotocol";
    }

    /// <summary>原生连接关闭的原因。值进指标标签，别随手改。</summary>
    public static class KinnaWebSocketCloseReasons
    {
        /// <summary>客户端发来关闭帧</summary>
        public const string ClientClosed = "client";

        /// <summary>待发队列满，慢客户端被踢</summary>
        public const string QueueFull = "queue_full";

        /// <summary>上行帧超过上限</summary>
        public const string FrameTooLarge = "frame_too_large";

        /// <summary>写循环发送失败（对端已断、socket 出错）</summary>
        public const string SendFailed = "send_failed";

        /// <summary>服务端已不认这条连接（租约被清扫、被别的副本踢掉）</summary>
        public const string Expired = "expired";

        /// <summary>服务端主动软踢后关闭</summary>
        public const string Terminated = "terminated";

        /// <summary>关闭帧在超时内没写出去（写循环已死或客户端根本不读）</summary>
        public const string CloseTimeout = "close_timeout";

        /// <summary>进程停机</summary>
        public const string Shutdown = "shutdown";

        /// <summary>停机时连 1012 都塞不进队列（慢客户端），只能硬断</summary>
        public const string ShutdownUnsent = "shutdown_unsent";
    }
}
