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
    /// 多实例安全靠原子认领而不是 Redis 锁，见 <see cref="OutboxSql.ClaimBatch"/>。
    /// 它是 Singleton（AddHostedService），不能构造注入 Scoped 服务 —— 每轮由
    /// <see cref="OutboxWorker"/> 自己开作用域解析。
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
                    // 后台服务里逃出去的异常会直接停掉整个宿主，投递失败不能拖垮业务进程
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
