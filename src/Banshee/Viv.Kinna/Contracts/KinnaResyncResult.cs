namespace Viv.Kinna.Contracts
{
    /// <summary>
    /// 重连对齐的结果。Link 不存消息，只能给出「服务端认为已经送到你这里的位置」，
    /// 具体要补哪些由客户端拿这个值和自己的最后一条去比。
    /// </summary>
    public sealed record KinnaResyncResult : KinnaPayload
    {
        /// <summary>
        /// 服务端记录的已确认水位，没有记录时为 null。
        /// </summary>
        public long? LastDeliveredMessageId { get; init; }

        /// <summary>
        /// 逐个记下的已确认消息 Id。只保留最近的一小批，记满后清空、只留水位；
        /// 客户端补拉时可以跳过这些。
        /// </summary>
        public long[] DeliveredMessageIds { get; init; } = [];

        /// <summary>
        /// true 表示客户端可能缺消息，应该走 HTTP 增量拉取。
        /// </summary>
        public bool NeedBackfill { get; init; }

        public DateTimeOffset ServerTime { get; init; }
    }
}
