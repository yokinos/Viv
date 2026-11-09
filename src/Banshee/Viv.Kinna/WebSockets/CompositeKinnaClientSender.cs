using Microsoft.AspNetCore.SignalR;
using Viv.Kinna.Connections;
using Viv.Kinna.Metrics;

namespace Viv.Kinna.WebSockets
{
    /// <summary>
    /// 按连接归属分流的发送器：本进程登记过原生连接就走队列，其余交给 SignalR。
    ///
    /// 分流依据是连接表而不是 Id 前缀——连接表是唯一事实，前缀只是约定，
    /// 一旦有人换个前缀命名就会把消息发丢，而 SignalR 对未知连接是静默丢弃，不会报错。
    ///
    /// <see cref="Options.KinnaOptions.EnableSignalR"/> 关掉时，落到 SignalR 那一侧的目标
    /// 没有兜底通道，只能丢弃。这种情况记 <c>kinna.send.orphaned</c>：它是配置形态正常的
    /// 结果（连接在别的副本或已失效），但长期非零要怀疑是不是漏开了 SignalR。
    /// </summary>
    internal sealed class CompositeKinnaClientSender<THub> : IKinnaClientSender where THub : Hub
    {
        private static readonly string HubName = typeof(THub).Name;

        private readonly KinnaWebSocketRegistry<THub> _webSockets;
        private readonly IKinnaClientSender _signalr;
        private readonly bool _signalrEnabled;

        public CompositeKinnaClientSender(
            KinnaWebSocketRegistry<THub> webSockets,
            IKinnaClientSender signalr,
            bool signalrEnabled = true)
        {
            _webSockets = webSockets;
            _signalr = signalr;
            _signalrEnabled = signalrEnabled;
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

            if (!_signalrEnabled)
            {
                KinnaMetrics.RecordOrphanedSend(HubName, KinnaSendDropReasons.SignalRDisabled);
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

            if (signalrIds is not { Count: > 0 })
            {
                return;
            }

            if (!_signalrEnabled)
            {
                // 按条记而不是按次记，和「实际丢了几条消息」对齐。
                // 一次 Add 累加 count，别在循环里逐条 Add：大群推送就是成百上千次分配
                KinnaMetrics.RecordOrphanedSend(
                    HubName, KinnaSendDropReasons.SignalRDisabled, signalrIds.Count);

                return;
            }

            await _signalr.SendToConnectionsAsync(signalrIds, method, payload, cancellationToken)
                .ConfigureAwait(false);
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

            if (!_signalrEnabled)
            {
                return;
            }

            await _signalr.SendToGroupAsync(groupName, method, payload, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 组表只在本进程有效，非本机连接在这张表里本来就没有位置。
        /// 没有 SignalR 时直接返回，不算「丢消息」，不计 orphaned。
        /// </summary>
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

            return _signalrEnabled
                ? _signalr.AddToGroupAsync(connectionId, groupName, cancellationToken)
                : Task.CompletedTask;
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

            return _signalrEnabled
                ? _signalr.RemoveFromGroupAsync(connectionId, groupName, cancellationToken)
                : Task.CompletedTask;
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

            if (!_signalrEnabled)
            {
                // 踢人指令到了一条本机不认识的连接上：它要么在别的副本，要么已经断了。
                // 跨副本踢人本来就靠广播，这里丢弃不影响正确性
                KinnaMetrics.RecordOrphanedSend(HubName, KinnaSendDropReasons.SignalRDisabled);
                return Task.CompletedTask;
            }

            return _signalr.TerminateConnectionAsync(connectionId, method, args, cancellationToken);
        }
    }
}
