namespace Viv.Kinna.Contracts
{
    /// <summary>
    /// 心跳应答，同时表示租约已续期。
    /// </summary>
    public sealed record KinnaPong : KinnaPayload
    {
        public DateTimeOffset ServerTime { get; init; }

        /// <summary>本次续期后的租约到期时间。服务端不再持有这条连接时等于 <see cref="ServerTime"/>（即已过期）。</summary>
        public DateTimeOffset LeaseExpiresAt { get; init; }

        /// <summary>
        /// 服务端是否还认这条连接。false 表示连接表里已经没有它了——通常是超租约被清扫，
        /// 或者被另一个副本踢掉。客户端收到后应当直接关掉重连，而不是继续用这条已经收不到消息的连接。
        /// </summary>
        public bool Alive { get; init; } = true;
    }
}
