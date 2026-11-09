using Microsoft.AspNetCore.SignalR;
using Viv.Contracts.Models;
using Viv.Kinna.Contracts;

namespace Viv.Kinna.Connections
{
    /// <summary>
    /// 连接生命周期和协议应答。Hub 依赖 Context，带不了单测，所以逻辑放这里，
    /// Hub 里只留身份解析和转发。
    ///
    /// <typeparamref name="THub"/> 区分模块，一个进程装几个 Hub 就有几套协调器。
    /// </summary>
    public interface IKinnaConnectionCoordinator<THub> where THub : Hub
    {
        /// <summary>
        /// 登记连接并加入所属群组。身份缺 AppId/SubjectId/UserId 时返回 null。
        /// </summary>
        /// <param name="connectionId">连接 Id</param>
        /// <param name="identity">已验签的身份</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <param name="onRegistered">
        /// 登记成功、但**尚未加组**时的回调，用来发握手帧（Connected）。
        /// 加组一完成，群推送就可能到达这条连接；等本方法返回再发握手帧，
        /// 客户端会先收到业务帧再收到 Connected。SignalR 传 <c>Clients.Caller.SendAsync</c>，
        /// 原生端点传 <c>registry.Send</c>。
        /// </param>
        Task<KinnaConnected?> RegisterAsync(
            string connectionId,
            VivContextContent identity,
            CancellationToken cancellationToken = default,
            Func<KinnaConnected, Task>? onRegistered = null);

        Task UnregisterAsync(string connectionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 心跳，顺带续租。
        /// </summary>
        Task<KinnaPong> PingAsync(string connectionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 投递确认：记录客户端已收到的消息。返回值区分「已记录 / 被限流 / 连接未知 / 落库失败」，
        /// 客户端据此决定要不要重报——只回一个水位的话，这四种情况没法区分。
        /// </summary>
        Task<KinnaAckResult> AckAsync(string connectionId, IReadOnlyList<long> messageIds, CancellationToken cancellationToken = default);

        /// <summary>
        /// 重连对齐：用已确认水位判断客户端要不要补拉。和其它上行指令共用配额，
        /// 被挡下时返回 <see cref="KinnaResyncResult.Throttled"/>。
        /// </summary>
        Task<KinnaResyncResult> ResyncAsync(string connectionId, long? lastMessageId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 先移出连接表，再下发软踢。
        /// </summary>
        Task KickAsync(string connectionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 踢掉某个用户的全部连接。
        /// </summary>
        Task KickUserAsync(long subjectId, long userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 只踢某个用户在某一个客户端上的连接（其它端不受影响）。
        ///
        /// 和 <see cref="KickUserAsync"/> 的区别只在索引：这个走「租户 + 用户 + 客户端」，
        /// 用于某一端 token 泄露、某一端版本有问题要强制重新登录。
        /// </summary>
        Task KickClientAsync(long appId, long subjectId, long userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 只踢本副本上该租户的连接，集群范围要另外走广播。
        /// </summary>
        Task KickSubjectAsync(long subjectId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 踢掉某个用户的全部连接，**所有副本**都生效。本机立刻踢，其余副本靠广播。
        ///
        /// 覆盖该用户的所有客户端（PC + 小程序 + …）。只想踢某一端用
        /// <see cref="KickClientEverywhereAsync"/>。
        ///
        /// 没注册 <see cref="Ports.IKinnaKickBus{THub}"/> 时退化成只踢本机——
        /// 封号的调用方不需要判断部署形态。
        /// </summary>
        /// <param name="subjectId">租户</param>
        /// <param name="userId">用户</param>
        /// <param name="reason">只进日志，便于追溯是谁触发的</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task KickUserEverywhereAsync(
            long subjectId,
            long userId,
            string? reason = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 只踢某个用户在某一个客户端上的连接，**所有副本**都生效。
        ///
        /// 某一端 token 泄露、某一端版本有问题要强制重新登录时用；
        /// 同一用户其它客户端照常在线。封号请用 <see cref="KickUserEverywhereAsync"/>。
        /// </summary>
        /// <param name="appId">客户端。两端并存时 AppId 必须不同，否则区分不开</param>
        /// <param name="subjectId">租户</param>
        /// <param name="userId">用户</param>
        /// <param name="reason">只进日志</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task KickClientEverywhereAsync(
            long appId,
            long subjectId,
            long userId,
            string? reason = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 踢掉某个租户下的全部连接，**所有副本**都生效。
        /// </summary>
        /// <param name="subjectId">租户</param>
        /// <param name="reason">只进日志</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task KickSubjectEverywhereAsync(
            long subjectId,
            string? reason = null,
            CancellationToken cancellationToken = default);
    }
}
