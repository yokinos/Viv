using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        private readonly TimeSpan _ttl;
        private readonly TimeSpan _interval;

        public KinnaConnectionLeaseSweeper(
            IKinnaSession<THub> session,
            IServiceScopeFactory scopes,
            ILoggerContract logger)
        {
            _session = session;
            _scopes = scopes;
            _logger = logger;

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
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                var removed = _session.Pool.SweepExpired(_ttl);
                if (removed.Count == 0) continue;

                await OfflineAsync(removed).ConfigureAwait(false);

                _logger.Warning($"[{_session.Hub}] 清理超租约连接 {removed.Count} 条");
            }
        }

        private async Task OfflineAsync(List<KinnaConnectionInfo> removed)
        {
            await using var scope = _scopes.CreateAsyncScope();

            var presence = scope.ServiceProvider.GetService<IKinnaPresenceSink<THub>>();
            if (presence == null) return;

            foreach (var info in removed)
            {
                await presence.OfflineAsync(info.SubjectId, info.UserId).ConfigureAwait(false);
            }
        }
    }
}
