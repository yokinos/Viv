namespace Viv.Kinna.Contracts
{
    /// <summary>
    /// 连接建立后的握手回执。
    /// </summary>
    public sealed record KinnaConnected : KinnaPayload
    {
        public string ConnectionId { get; init; } = string.Empty;

        /// <summary>
        /// 服务端时间，客户端拿它校正本地时钟。
        /// </summary>
        public DateTimeOffset ServerTime { get; init; }

        /// <summary>
        /// 本副本记得的最后投递水位。null 表示不知道，客户端自己去业务 API 对齐。
        /// 这里不存消息，也就不假装能补。
        /// </summary>
        public long? ResumeHint { get; init; }
    }
}
