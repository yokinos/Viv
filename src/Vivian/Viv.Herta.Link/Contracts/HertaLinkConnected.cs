namespace Viv.Herta.Link.Contracts
{
    public sealed record HertaLinkConnected
    {
        public int V { get; init; } = 1;

        public string ConnectionId { get; init; } = string.Empty;

        public DateTimeOffset ServerTime { get; init; }

        /// <summary>
        /// 本副本记忆的最后投递水位。null = 客户端应自行与 Herta.Api 对齐。
        /// </summary>
        public long? ResumeHint { get; init; }
    }
}
