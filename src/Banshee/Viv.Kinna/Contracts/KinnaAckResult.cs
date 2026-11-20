namespace Viv.Kinna.Contracts
{
    /// <summary>
    /// 投递确认的处理结果。
    ///
    /// 以前这些情况都回一个 <c>0</c>（水位），客户端没法区分「记下了但没新水位」和
    /// 「根本没收下」——前者不用重报，后者必须重报，混在一起就只能一律重报或者一律不管。
    /// </summary>
    public enum KinnaAckStatus
    {
        /// <summary>已记录，<see cref="KinnaAckResult.Watermark"/> 是记完后的水位</summary>
        Accepted = 0,

        /// <summary>超出本连接的上行配额，本次没记。稍后重报即可，不会丢消息</summary>
        Throttled = 1,

        /// <summary>服务端不认这条连接（已超租约被清扫、或被别的副本踢掉），应当重连再报</summary>
        UnknownConnection = 2,

        /// <summary>落库失败，客户端应当重报</summary>
        Failed = 3
    }

    /// <summary>投递确认的应答。仅裸 WebSocket 会显式回帧，SignalR 里就是方法返回值。</summary>
    public sealed record KinnaAckResult : KinnaPayload
    {
        public KinnaAckStatus Status { get; init; }

        /// <summary>记录后的水位。只有 <see cref="KinnaAckStatus.Accepted"/> 时有意义</summary>
        public long Watermark { get; init; }

        /// <summary>
        /// 建议的最短退避时间（毫秒）。只在 <see cref="KinnaAckStatus.Throttled"/> 时有值，其余为 0。
        ///
        /// 它是**下限**不是精确值：客户端要在它之上叠加自己的指数退避。严格按这个值重试，
        /// 窗口翻转的那一毫秒所有被挡的连接会同时回来，下一个窗口立刻又满。
        ///
        /// 量级能区分原因，不需要额外的原因字段：上行配额用尽 → 窗口剩余（默认最多 10 秒）；
        /// 服务端串行队列满（下游慢）→ 几百毫秒。前者是自己的问题，后者该等得更久一点。
        /// </summary>
        public int RetryAfterMs { get; init; }
    }
}
