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

        public Task SendToGroupAsync(string groupName, string method, object? payload, CancellationToken cancellationToken = default)
            => _hub.Clients.Group(groupName).SendCoreAsync(method, [payload], cancellationToken);

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => _hub.Groups.AddToGroupAsync(connectionId, groupName, cancellationToken);

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => _hub.Groups.RemoveFromGroupAsync(connectionId, groupName, cancellationToken);
    }
}
