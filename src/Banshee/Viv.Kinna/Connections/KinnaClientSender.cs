using Microsoft.AspNetCore.SignalR;

namespace Viv.Kinna.Connections
{
    /// <summary>
    /// 基于 SignalR 的发送实现
    /// </summary>
    internal sealed class KinnaClientSender<THub> : IKinnaClientSender where THub : Hub
    {
        private readonly IHubContext<THub> _hub;

        public KinnaClientSender(IHubContext<THub> hub)
        {
            _hub = hub;
        }

        public Task SendToConnectionAsync(string connectionId, string method, object?[] args, CancellationToken cancellationToken = default)
            => _hub.Clients.Client(connectionId).SendCoreAsync(method, args, cancellationToken);

        public Task SendToConnectionsAsync(IReadOnlyList<string> connectionIds, string method, object? payload, CancellationToken cancellationToken = default)
            => _hub.Clients.Clients(connectionIds).SendCoreAsync(method, [payload], cancellationToken);

        /// <summary>
        /// 组播。有排除项时走 <c>GroupExcept</c>：SignalR 的组广播本身不排除任何成员，
        /// 发消息的那条连接也在组里，不剔掉它就会收到自己刚发的消息。
        /// </summary>
        public Task SendToGroupAsync(
            string groupName,
            string method,
            object? payload,
            IReadOnlyList<string>? excludedConnectionIds = null,
            CancellationToken cancellationToken = default)
            => excludedConnectionIds is { Count: > 0 }
                ? _hub.Clients.GroupExcept(groupName, excludedConnectionIds)
                    .SendCoreAsync(method, [payload], cancellationToken)
                : _hub.Clients.Group(groupName)
                    .SendCoreAsync(method, [payload], cancellationToken);

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => _hub.Groups.AddToGroupAsync(connectionId, groupName, cancellationToken);

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => _hub.Groups.RemoveFromGroupAsync(connectionId, groupName, cancellationToken);

        /// <summary>
        /// SignalR 没有「服务端主动断开某条连接」的 API（HubLifetimeManager 只管发消息），
        /// 所以这里退化成软踢：帧发出去，剩下的交给客户端自己 stop。
        /// </summary>
        public Task TerminateConnectionAsync(string connectionId, string method, object?[] args, CancellationToken cancellationToken = default)
            => SendToConnectionAsync(connectionId, method, args, cancellationToken);
    }
}
