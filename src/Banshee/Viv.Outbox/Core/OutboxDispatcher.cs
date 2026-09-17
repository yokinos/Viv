using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Viv.Log;
using Viv.Outbox.Options;

namespace Viv.Outbox.Core
{
    /// <summary>
    /// 发件箱投递器的后台宿主：起一轮、睡一会儿、再来一轮。
    ///
    /// <para>
    /// <b>多实例安全靠原子认领，不靠 Redis 锁</b>：同一个服务起 3 个副本 + Worker 一起跑，
    /// 数据库的 <c>UPDATE ... OUTPUT/RETURNING</c> 保证一行只会被一个实例拿到。
    /// </para>
    ///
    /// <para>
    /// <b>它是 Singleton（AddHostedService），所以不能构造注入任何 Scoped 服务</b>
    /// （<c>IVivEventPublisher</c> / <c>IOutboxRepository</c> 都是 Scoped）——
    /// 每轮由 <see cref="OutboxWorker"/> 自己开作用域去解析。
    /// </para>
    /// </summary>
    internal sealed class OutboxDispatcher : BackgroundService
    {
        private readonly OutboxWorker _worker;
        private readonly ILoggerContract _logger;
        private readonly OutboxOptions _options;

        public OutboxDispatcher(
            IServiceScopeFactory scopeFactory,
            IOutboxEnvelopeSenderFactory senderFactory,
            ILoggerContract logger,
            IOptions<OutboxOptions> options)
        {
            _logger = logger;
            _options = options.Value;
            _worker = new OutboxWorker(scopeFactory, senderFactory, logger, _options);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var pollDelay = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));

            if (!await _worker.StartupAsync(stoppingToken).ConfigureAwait(false))
            {
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var delivered = await _worker.RunOnceAsync(stoppingToken).ConfigureAwait(false);
                    if (delivered > 0)
                    {
                        _logger.Info($"发件箱本轮投递 {delivered} 条");
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // 后台服务里逃出去的异常在 .NET 6+ 会**直接停掉整个宿主**。
                    // 投递失败绝不能拖垮业务进程：吞掉、记日志、下一轮再来。
                    _logger.Error("发件箱投递轮次异常（已吞掉，不影响宿主机）", ex);
                }

                try
                {
                    await Task.Delay(pollDelay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
