namespace Viv.Kinna.Contracts
{
    /// <summary>
    /// 原生 WebSocket 的子协议名。客户端在 <c>Sec-WebSocket-Protocol</c> 里声明，服务端回同名，
    /// 双方在 101 之前就确认「说的是同一种话」。
    ///
    /// 目前只有 v1。上行帧格式以后要改（比如 Ack 带上批次号）就新增 v2、按协商结果分派，
    /// 不必让服务端同时维护多套解析。
    /// </summary>
    public static class KinnaWebSocketProtocols
    {
        /// <summary>当前版本。</summary>
        public const string V1 = "kinna.v1";
    }
}
