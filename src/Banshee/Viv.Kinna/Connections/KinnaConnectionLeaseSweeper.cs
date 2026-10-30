using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Viv.Kinna.Contracts;
using Viv.Kinna.Ports;
using Viv.Log;

namespace Viv.Kinna.Connections
{
    /// <summary>
    /// 定期清掉超过租约的连接。去掉 Redis 背板以后连接表没有 TTL，
    /// 客户端断网但 TCP 没断时 OnDisconnectedAsync 不会触发，只能靠 Ping 续租。
    /// 进程整体退出时表跟着消失，不需要清理。
    ///
    /// 清掉的连接要顺带扣在线态，否则 Redis 上的计数只增不减，键过期后
    /// 还活着的连接再也恢复不了在线。在线态端口是 Scoped，所以这里开作用域取。
    ///
    /// 每个 Hub 一个清扫循环。
    /// </summary>
    public sealed class KinnaConnectionLeaseSweeper<THub> : BackgroundService where THub : Hub
    {
        private readonly IKinnaSession<THub> _session;
        private readonly IServiceScopeFactory _scopes;
        private readonly ILoggerContract _logger;
        private readonly KinnaSweepClock<THub> _clock;
        private readonly TimeSpan _ttl;
        private readonly TimeSpan _interval;

        public KinnaConnectionLeaseSweeper(
            IKinnaSession<THub> session,
            IServiceScopeFactory scopes,
            ILoggerContract logger,
            KinnaSweepClock<THub>? clock = null)
        {
            _session = session;
            _scopes = scopes;
            _logger = logger;
            _clock = clock ?? new KinnaSweepClock<THub>();

            var options = session.Options;
            _ttl = TimeSpan.FromSeconds(Math.Max(1, options.EntryTtlSeconds));
            _interval = TimeSpan.FromSeconds(Math.Max(1, options.SweepIntervalSeconds));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_interval, stoppingToken).ConfigureAwait(false);
                    await SweepOnceAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // 清扫抛出去会把整个宿主停掉。这一轮失败，下一轮再扫。
                    _logger.Error($"[{_session.Hub}] 连接清扫失败", ex);
                }
            }
        }

        /// <summary>扫一轮：摘表、扣在线态、软踢。软踢之后客户端重连才会重新 Register。</summary>
        internal async Task SweepOnceAsync(CancellationToken cancellationToken = default)
        {
            var removed = _session.Pool.SweepExpired(_ttl);
            _clock.Record(removed.Count);

            if (removed.Count == 0) return;

            await DisconnectAsync(removed, cancellationToken).ConfigureAwait(false);

            _logger.Warning($"[{_session.Hub}] 清理超租约连接 {removed.Count} 条");
        }

        private async Task DisconnectAsync(List<KinnaConnectionInfo> removed, CancellationToken cancellationToken)
        {
            await using var scope = _scopes.CreateAsyncScope();
            var presence = scope.ServiceProvider.GetService<IKinnaPresenceSink<THub>>();

            foreach (var info in removed)
            {
                if (presence != null)
                {
                    try
                    {
                        await presence.OfflineAsync(info.SubjectId, info.UserId).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.Error($"[{_session.Hub}] 连接 {info.ConnectionId} 扣在线态失败", ex);
                    }
                }

                try
                {
                    await _session.Sender
                        .SendToConnectionAsync(info.ConnectionId, KinnaClientMethods.ForceDisconnect, [], cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.Error($"[{_session.Hub}] 连接 {info.ConnectionId} 软踢失败", ex);
                }
            }
        }
    }
}
