using Microsoft.AspNetCore.SignalR;
using Viv.Kinna.Connections;

namespace Viv.Kinna.WebSockets
{
    /// <summary>
    /// 按连接归属分流的发送器：本进程登记过原生连接就走队列，其余交给 SignalR。
    ///
    /// 分流依据是连接表而不是 Id 前缀——连接表是唯一事实，前缀只是约定，
    /// 一旦有人换个前缀命名就会把消息发丢，而 SignalR 对未知连接是静默丢弃，不会报错。
    /// </summary>
    internal sealed class CompositeKinnaClientSender<THub> : IKinnaClientSender where THub : Hub
    {
        private readonly KinnaWebSocketRegistry<THub> _webSockets;
        private readonly IKinnaClientSender _signalr;

        public CompositeKinnaClientSender(KinnaWebSocketRegistry<THub> webSockets, IKinnaClientSender signalr)
        {
            _webSockets = webSockets;
            _signalr = signalr;
        }

        public Task SendToConnectionAsync(
            string connectionId,
            string method,
            object?[] args,
            CancellationToken cancellationToken = default)
        {
            if (_webSockets.IsLocal(connectionId))
            {
                _webSockets.Send(connectionId, method, args);
                return Task.CompletedTask;
            }

            return _signalr.SendToConnectionAsync(connectionId, method, args, cancellationToken);
        }

        public async Task SendToConnectionsAsync(
            IReadOnlyList<string> connectionIds,
            string method,
            object? payload,
            CancellationToken cancellationToken = default)
        {
            if (connectionIds.Count == 0)
            {
                return;
            }

            List<string>? nativeIds = null;
            List<string>? signalrIds = null;

            foreach (var connectionId in connectionIds)
            {
                if (_webSockets.IsLocal(connectionId))
                {
                    (nativeIds ??= []).Add(connectionId);
                }
                else
                {
                    (signalrIds ??= []).Add(connectionId);
                }
            }

            if (nativeIds is { Count: > 0 })
            {
                _webSockets.SendMany(nativeIds, method, [payload]);
            }

            if (signalrIds is { Count: > 0 })
            {
                await _signalr.SendToConnectionsAsync(signalrIds, method, payload, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 两边都发。组名相同但成员分处两张表，发到没有成员的那一侧是空操作，不用先判断。
        /// </summary>
        public async Task SendToGroupAsync(
            string groupName,
            string method,
            object? payload,
            CancellationToken cancellationToken = default)
        {
            _webSockets.SendToGroup(groupName, method, [payload]);

            await _signalr.SendToGroupAsync(groupName, method, payload, cancellationToken)
                .ConfigureAwait(false);
        }

        public Task AddToGroupAsync(
            string connectionId,
            string groupName,
            CancellationToken cancellationToken = default)
        {
            if (_webSockets.IsLocal(connectionId))
            {
                _webSockets.AddToGroup(connectionId, groupName);
                return Task.CompletedTask;
            }

            return _signalr.AddToGroupAsync(connectionId, groupName, cancellationToken);
        }

        public Task RemoveFromGroupAsync(
            string connectionId,
            string groupName,
            CancellationToken cancellationToken = default)
        {
            if (_webSockets.IsLocal(connectionId))
            {
                _webSockets.RemoveFromGroup(connectionId, groupName);
                return Task.CompletedTask;
            }

            return _signalr.RemoveFromGroupAsync(connectionId, groupName, cancellationToken);
        }

        /// <summary>
        /// 原生连接走「入队软踢帧 + 排空后硬断」，SignalR 只能软踢。
        /// SignalR 一侧返回 false（没有本地 socket）时仍然要发帧，所以不判断分流结果。
        /// </summary>
        public Task TerminateConnectionAsync(
            string connectionId,
            string method,
            object?[] args,
            CancellationToken cancellationToken = default)
        {
            if (_webSockets.Terminate(connectionId, method, args))
            {
                return Task.CompletedTask;
            }

            return _signalr.TerminateConnectionAsync(connectionId, method, args, cancellationToken);
        }
    }
}
