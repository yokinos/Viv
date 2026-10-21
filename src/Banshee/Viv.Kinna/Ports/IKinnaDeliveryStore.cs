using Microsoft.AspNetCore.SignalR;

namespace Viv.Kinna.Ports
{
    /// <summary>
    /// 客户端已确认的投递状态。水位是「收到过的最大的 Id」，Ids 是最近逐个记下的那些；
    /// 记满之后只留水位，Ids 清空（相当于全部标记已收到）。
    /// </summary>
    public readonly record struct KinnaDeliveryState(long? Watermark, IReadOnlyList<long> MessageIds)
    {
        public static readonly KinnaDeliveryState Empty = new(null, []);
    }

    /// <summary>
    /// 投递确认的存储。Kinna 不绑存储实现（不引 Redis、不碰数据库），
    /// 留这个口子给业务：Herta 落在 Redis，别的模块落在自己的库也行。
    /// </summary>
    public interface IKinnaDeliveryStore<THub> where THub : Hub
    {
        /// <summary>记录客户端已确认的消息，返回记录后的水位。</summary>
        Task<long> AckAsync(long subjectId, long userId, IReadOnlyList<long> messageIds, CancellationToken cancellationToken = default);

        /// <summary>没有记录时返回 <see cref="KinnaDeliveryState.Empty"/>。</summary>
        Task<KinnaDeliveryState> GetDeliveredAsync(long subjectId, long userId, CancellationToken cancellationToken = default);
    }

    /// <summary>没配存储时的默认实现：什么都不记，Resync 一律要求补拉。</summary>
    public sealed class NoopKinnaDeliveryStore<THub> : IKinnaDeliveryStore<THub> where THub : Hub
    {
        public static readonly NoopKinnaDeliveryStore<THub> Instance = new();

        public Task<long> AckAsync(long subjectId, long userId, IReadOnlyList<long> messageIds, CancellationToken cancellationToken = default)
            => Task.FromResult(0L);

        public Task<KinnaDeliveryState> GetDeliveredAsync(long subjectId, long userId, CancellationToken cancellationToken = default)
            => Task.FromResult(KinnaDeliveryState.Empty);
    }
}
