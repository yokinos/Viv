namespace Viv.Kinna.Contracts
{
    /// <summary>
    /// 上行消息（客户端 → 服务端）。原生 WebSocket 没有 RPC 语义，
    /// 客户端发一个指令、服务端回一个带方法名的结果帧，靠 <see cref="Type"/> 区分。
    /// </summary>
    public sealed class KinnaClientMessage
    {
        /// <summary>指令名，取 <see cref="KinnaInboundMethods"/> 里的常量</summary>
        public string? Type { get; set; }

        /// <summary>投递确认用的消息 Id 列表</summary>
        public long[]? Ids { get; set; }

        /// <summary>重连对齐用的客户端最后一条消息 Id</summary>
        public long? LastMessageId { get; set; }
    }

    /// <summary>
    /// 上行指令名。不认识的一律忽略，按开放集合处理。
    /// </summary>
    public static class KinnaInboundMethods
    {
        /// <summary>心跳 + 续租，回 <see cref="KinnaPong"/></summary>
        public const string Ping = "ping";

        /// <summary>投递确认，回当前水位</summary>
        public const string Ack = "ack";

        /// <summary>重连对齐，回 <see cref="KinnaResyncResult"/></summary>
        public const string Resync = "resync";
    }
}
