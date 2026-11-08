namespace Viv.Kinna.Contracts
{
    /// <summary>
    /// 原生 WebSocket 的子协议名。客户端在 <c>Sec-WebSocket-Protocol</c> 里声明，服务端回同名，
    /// 双方在 101 之前就确认「说的是同一种话」。
    ///
    /// 上行帧格式以后要改（比如 Ack 带上批次号）时新增 v2，服务端按协商结果分派；
    /// 没有这个入口的话，只能靠「服务端同时兼容新旧格式」往下拖，拖到第三个版本就没人敢动。
    /// </summary>
    public static class KinnaWebSocketProtocols
    {
        /// <summary>当前版本。</summary>
        public const string V1 = "kinna.v1";
    }
}
