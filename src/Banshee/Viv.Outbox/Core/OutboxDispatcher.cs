using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Viv.Log;
using Viv.Outbox.Options;

namespace Viv.Outbox.Core
{
    /// <summary>
    /// 发件箱投递器的后台宿主：起一轮、等一会儿、再来一轮。
    ///
    /// 「等一会儿」不是死等：入队提交后 <see cref="OutboxCommitObserver"/> 会通过
    /// <see cref="OutboxSignal"/> 把这一轮提前叫起来，所以轮询间隔退化成<b>兜底</b> ——
    /// 它现在只负责两件事：接住别的进程写进来的消息（进程内信号跨不过去），
    /// 以及接住信号丢失（进程刚起、通知方被回滚等）。真正决定延迟的是"提交后多久被唤醒"。
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
        private readonly TimeProvider _time;
        private readonly OutboxSignal _signal;

        public OutboxDispatcher(
            IServiceScopeFactory scopeFactory,
            IOutboxEnvelopeSenderFactory senderFactory,
            ILoggerContract logger,
            IOptions<OutboxOptions> options,
            TimeProvider? timeProvider = null,
            OutboxSignal? signal = null)
        {
            _logger = logger;
            _options = options.Value;
            _time = timeProvider ?? TimeProvider.System;

            // 没有信号源时用"永不来信号"的单例：等待逻辑只剩一条分支，不必再为 null 写一套延时
            _signal = signal ?? OutboxSignal.Never;

            // 同一个时钟交给 worker，否则「本轮认领的时刻」和「轮询间隔」会来自两个时间源
            _worker = new OutboxWorker(scopeFactory, senderFactory, logger, _options, _time);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var pollDelay = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));

            while (!stoppingToken.IsCancellationRequested)
            {
                if (await _worker.StartupAsync(stoppingToken).ConfigureAwait(false))
                {
                    break;
                }

                if (stoppingToken.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    await Task.Delay(pollDelay, _time, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
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
                    // 有信号立刻进下一轮（入队刚提交），没信号就等到兜底间隔到点。
                    // 返回值只用来区分这两条路径，循环体对它们没差别 —— 都只是"再来一轮"。
                    await _signal.WaitAsync(pollDelay, _time, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}