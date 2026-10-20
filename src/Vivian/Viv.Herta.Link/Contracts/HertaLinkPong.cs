namespace Viv.Herta.Link.Contracts
{
    public sealed record HertaLinkPong
    {
        public int V { get; init; } = 1;

        /// <summary>服务端时间，客户端据此校正本地时钟</summary>
        public DateTimeOffset ServerTime { get; init; }

        /// <summary>本次 Ping 续期后的连接租约到期时刻</summary>
        public DateTimeOffset LeaseExpiresAt { get; init; }
    }
}
