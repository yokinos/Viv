namespace Viv.Kinna.Contracts
{
    /// <summary>
    /// 下行方法名，服务端到客户端。方法名即事件类型，载荷都要带 V。
    /// 客户端约定：不认识的方法和字段忽略掉。
    /// </summary>
    public static class KinnaClientMethods
    {
        /// <summary>
        /// 握手回执，载荷 <see cref="KinnaConnected"/>
        /// </summary>
        public const string Connected = "Connected";

        /// <summary>
        /// 新消息，载荷由业务定
        /// </summary>
        public const string ReceiveMessage = "ReceiveMessage";

        /// <summary>
        /// 消息撤回，载荷由业务定
        /// </summary>
        public const string MessageRecalled = "MessageRecalled";

        /// <summary>
        /// 软踢，客户端收到后自己 stop
        /// </summary>
        public const string ForceDisconnect = "ForceDisconnect";

        /// <summary>
        /// 心跳应答，载荷 <see cref="KinnaPong"/>。
        /// SignalR 里 Ping 是方法返回值，裸 WebSocket 没有 RPC 语义，得显式回一帧。
        /// </summary>
        public const string Pong = "Pong";

        /// <summary>
        /// 投递确认的结果，载荷 <see cref="KinnaAckResult"/>（状态 + 水位）。仅裸 WebSocket 用。
        /// </summary>
        public const string Ack = "Ack";

        /// <summary>
        /// 重连对齐的结果，载荷 <see cref="KinnaResyncResult"/>。仅裸 WebSocket 用。
        /// </summary>
        public const string Resync = "Resync";
    }
}
