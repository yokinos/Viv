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
        Task<KinnaConnected?> RegisterAsync(string connectionId, VivContextContent identity, CancellationToken cancellationToken = default);

        Task UnregisterAsync(string connectionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 心跳，顺带续租。
        /// </summary>
        Task<KinnaPong> PingAsync(string connectionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 投递确认：记录客户端已收到的消息，返回当前水位。连接不在本机时返回 0。
        /// </summary>
        Task<long> AckAsync(string connectionId, IReadOnlyList<long> messageIds, CancellationToken cancellationToken = default);

        /// <summary>
        /// 重连对齐：用已确认水位判断客户端要不要补拉。
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
        /// 只踢本副本上该租户的连接，集群范围要另外走广播。
        /// </summary>
        Task KickSubjectAsync(long subjectId, CancellationToken cancellationToken = default);
    }
}
