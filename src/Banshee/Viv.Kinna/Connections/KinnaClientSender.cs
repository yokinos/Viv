using Microsoft.AspNetCore.SignalR;

namespace Viv.Kinna.Connections
{
    /// <summary>
    /// 基于 SignalR 的发送实现。internal 是故意的：业务拿到接口就够，
    /// 看的到具体类型容易绕过接口直接用 Hub。
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

        public Task SendToGroupAsync(string groupName, string method, object? payload, CancellationToken cancellationToken = default)
            => _hub.Clients.Group(groupName).SendCoreAsync(method, [payload], cancellationToken);

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => _hub.Groups.AddToGroupAsync(connectionId, groupName, cancellationToken);

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => _hub.Groups.RemoveFromGroupAsync(connectionId, groupName, cancellationToken);
    }
}
