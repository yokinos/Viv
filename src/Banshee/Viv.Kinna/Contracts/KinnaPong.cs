namespace Viv.Kinna.Contracts
{
    /// <summary>
    /// 心跳应答，同时表示租约已续期。
    /// </summary>
    public sealed record KinnaPong : KinnaPayload
    {
        public DateTimeOffset ServerTime { get; init; }

        /// <summary>本次续期后的租约到期时间。</summary>
        public DateTimeOffset LeaseExpiresAt { get; init; }
    }
}
