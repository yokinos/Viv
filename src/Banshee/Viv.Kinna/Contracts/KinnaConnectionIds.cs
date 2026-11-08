namespace Viv.Kinna.Contracts
{
    /// <summary>
    /// 连接 Id 的生成与判别。
    ///
    /// 前缀是排障约定：看一眼 Id 就知道这条连接是从哪个入口进来的。原生 WebSocket 的 Id 由
    /// 端点自己生成，SignalR 的 Id 由框架生成，两边不冲突。
    /// </summary>
    public static class KinnaConnectionIds
    {
        /// <summary>原生 WebSocket 连接 Id 的前缀</summary>
        public const string WebSocketPrefix = "ws:";

        /// <summary>这条连接是否来自原生 WebSocket 端点</summary>
        public static bool IsWebSocket(string connectionId)
            => connectionId.StartsWith(WebSocketPrefix, StringComparison.Ordinal);

        public static string NewWebSocket() => WebSocketPrefix + Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// 指标标签 <c>transport</c> 的取值。两套传输共用一本连接表，
    /// 不分开打标签的话，排障时分不清连接数是被哪一套撑起来的。
    /// </summary>
    public static class KinnaTransports
    {
        public const string SignalR = "signalr";

        public const string WebSocket = "websocket";

        /// <summary>按连接 Id 前缀推断传输类型</summary>
        public static string Of(string connectionId)
            => KinnaConnectionIds.IsWebSocket(connectionId) ? WebSocket : SignalR;
    }
}
