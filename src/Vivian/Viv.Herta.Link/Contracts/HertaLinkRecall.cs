namespace Viv.Herta.Link.Contracts
{
    public sealed record HertaLinkRecall
    {
        public int V { get; init; } = 1;

        public long MessageId { get; init; }

        public DateTimeOffset RecalledAt { get; init; }
    }
}
