namespace Viv.Herta.Link.Contracts
{
    /// <summary>
    /// Link 不存消息，所以这里只能回答要不要补拉，不能替客户端补。
    /// </summary>
    public sealed record HertaLinkResyncResult
    {
        public int V { get; init; } = 1;

        /// <summary>本副本记忆的水位；null = 不知道</summary>
        public long? LastDeliveredMessageId { get; init; }

        /// <summary>true = 客户端应向 Herta.Api 增量拉取</summary>
        public bool NeedBackfill { get; init; }
    }
}
