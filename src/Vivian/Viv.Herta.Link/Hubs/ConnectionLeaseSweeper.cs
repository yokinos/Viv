using Microsoft.Extensions.Options;
using Viv.Herta.Link.Options;
using Viv.Log;

namespace Viv.Herta.Link.Hubs
{
    /// <summary>
    /// 定时剔除超租约的连接。没有 Redis 背板后连接表没有 TTL 兜底：
    /// 客户端断网但 TCP 未断时 OnDisconnectedAsync 不会触发。
    /// </summary>
    public class ConnectionLeaseSweeper : BackgroundService
    {
        private readonly IConnectionPool _pool;
        private readonly ILoggerContract _logger;
        private readonly TimeSpan _ttl;
        private readonly TimeSpan _interval;

        public ConnectionLeaseSweeper(IConnectionPool pool, IOptions<HertaLinkOptions> options, ILoggerContract logger)
        {
            _pool = pool;
            _logger = logger;

            var value = options.Value;
            _ttl = TimeSpan.FromSeconds(Math.Max(1, value.EntryTtlSeconds));
            _interval = TimeSpan.FromSeconds(Math.Max(1, value.SweepIntervalSeconds));
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

                var removed = _pool.SweepExpired(_ttl);
                if (removed.Count > 0)
                    _logger.Warning($"清理超租约连接 {removed.Count} 条");
            }
        }
    }
}
