using Viv.Kinna.Contracts;

namespace Viv.Herta.Link.Contracts
{
    /// <summary>撤回通知（<c>MessageRecalled</c> 载荷）。</summary>
    public sealed record HertaLinkRecall : KinnaPayload
    {
        public long MessageId { get; init; }

        public DateTimeOffset RecalledAt { get; init; }
    }
}
