using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Viv.Delusion.Magic;
using Viv.Log;
using Viv.Nana;
using Viv.Outbox.Options;

namespace Viv.Outbox.Core
{
    /// <summary>
    /// 发件箱投递的<b>一轮工作</b>：起表 → 释放过期租约 → 排空认领并投递 → 清理。
    ///
    /// <para>
    /// 与 <see cref="OutboxDispatcher"/> 分开是为了可测：调度（循环、睡眠、吞异常）没什么可测的，
    /// 业务语义（认领、退避、置 Failed、清理）全在这一层，可以在没有宿主、没有数据库的情况下直接跑。
    /// </para>
    /// </summary>
    internal sealed class OutboxWorker
    {
        private const int BackoffBaseMilliseconds = 5 * 1000;
        private const int BackoffMaxMilliseconds = 60 * 1000;
        private const double BackoffJitterRatio = 0.3;

        /// <summary>单轮清理最多删几批 —— 防的不是积压，是配置写错（保留期配成 0 天）时整轮耗在清理上。</summary>
        private const int CleanupMaxBatches = 50;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IOutboxEnvelopeSenderFactory _senderFactory;
        private readonly ILoggerContract _logger;
        private readonly OutboxOptions _options;

        public OutboxWorker(
            IServiceScopeFactory scopeFactory,
            IOutboxEnvelopeSenderFactory senderFactory,
            ILoggerContract logger,
            OutboxOptions options)
        {
            _scopeFactory = scopeFactory;
            _senderFactory = senderFactory;
            _logger = logger;
            _options = options;
        }

        /// <summary>建表并打一行启动日志。<returns>false = 起不来，投递器不该继续跑。</returns></summary>
        public async Task<bool> StartupAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();

                if (_options.AutoCreateTable)
                {
                    await repository.EnsureTableAsync(cancellationToken).ConfigureAwait(false);
                }

                _logger.Info(
                    $"发件箱投递器已启动：轮询 {_options.PollIntervalSeconds}s / 批 {_options.BatchSize} / " +
                    $"最大重试 {_options.MaxRetryCount} / 租约 {_options.LeaseSeconds}s / 保留 {_options.RetentionDays} 天");
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                // 表都建不出来还继续跑，只会每轮刷一条一模一样的错误，把真正的问题埋掉
                _logger.Error("发件箱投递器启动失败，不再运行 —— 发件箱里的消息会只进不出", ex);
                return false;
            }
        }

        /// <summary>跑一轮。<returns>本轮认领并尝试投递的条数（测试与日志用）。</returns></summary>
        public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();

            var repository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
            var publisher = scope.ServiceProvider.GetRequiredService<IVivEventPublisher>();

            // 1) 崩溃恢复：上次进程被 kill 时卡在 Processing 的行，租约一过就退回 Pending
            await repository.ReleaseExpiredLeasesAsync(DateTime.UtcNow, cancellationToken).ConfigureAwait(false);

            var batchSize = Math.Max(1, _options.BatchSize);
            var delivered = 0;

            // 2) 排空：一直认领到认不出为止 —— 只看一批的话，积压时投递速度会被轮询间隔卡死
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;
                var leaseUntil = now.AddSeconds(Math.Max(1, _options.LeaseSeconds));

                var batch = await repository
                    .ClaimBatchAsync(batchSize, now, leaseUntil, cancellationToken)
                    .ConfigureAwait(false);

                if (batch.Count == 0) break;

                foreach (var message in batch)
                {
                    await DeliverAsync(repository, publisher, message, cancellationToken).ConfigureAwait(false);
                }

                delivered += batch.Count;

                // 不满一批 = 已经认空了，不必再多跑一次往返
                if (batch.Count < batchSize) break;
            }

            // 3) 清理已投递且超过保留期的行
            await CleanupAsync(repository, cancellationToken).ConfigureAwait(false);

            return delivered;
        }

        internal async Task DeliverAsync(
            IOutboxRepository repository,
            IVivEventPublisher publisher,
            OutboxMessage message,
            CancellationToken cancellationToken = default)
        {
            var sender = _senderFactory.Resolve(message.EventType);
            if (sender is null)
            {
                // 事件类型解析不出来 = 这条消息永远发不出去。置 Failed 并记 Error，**绝不静默丢**：
                // 多半是 EventType 写错，或者事件类型所在程序集没被加载。
                _logger.Error(
                    $"发件箱消息的事件类型解析不出来，置为 Failed：EventType={message.EventType}, " +
                    $"MessageId={message.MessageId}, Id={message.Id}");
                await repository
                    .MarkFailedAsync(message.Id, message.RetryCount, $"未知事件类型：{message.EventType}", cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            try
            {
                await sender.SendAsync(publisher, message.Payload, message.MessageId, cancellationToken).ConfigureAwait(false);
                await repository.MarkSentAsync(message.Id, DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 停机：不消耗重试次数。租约到期后这条自然会被重新认领。
                throw;
            }
            catch (Exception ex)
            {
                await HandleFailureAsync(repository, message, ex, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 投递失败一律先重试 —— 不区分「MQ 挂了」和「序列化/路由问题」，
        /// 两类在这里的处理本来就是同一个：记 LastError + 退避重投，到上限才置 Failed。
        /// </summary>
        private async Task HandleFailureAsync(
            IOutboxRepository repository, OutboxMessage message, Exception ex, CancellationToken cancellationToken)
        {
            var maxRetryCount = Math.Max(1, _options.MaxRetryCount);
            var retryCount = message.RetryCount + 1;

            if (retryCount >= maxRetryCount)
            {
                _logger.Error(
                    $"发件箱消息重试 {retryCount} 次仍失败，置为 Failed（等人工介入）：" +
                    $"MessageId={message.MessageId}, Id={message.Id}, EventType={message.EventType}",
                    ex);
                await repository.MarkFailedAsync(message.Id, retryCount, ex.Message, cancellationToken).ConfigureAwait(false);
                return;
            }

            var nextRetryAt = DateTime.UtcNow.Add(Backoff(retryCount));
            _logger.Warning(
                $"发件箱消息投递失败，第 {retryCount}/{maxRetryCount} 次重试将于 {nextRetryAt:yyyy-MM-dd HH:mm:ss} UTC 进行：" +
                $"MessageId={message.MessageId}, Id={message.Id}, {ex.Message}");
            await repository
                .MarkPendingAsync(message.Id, retryCount, nextRetryAt, ex.Message, cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task CleanupAsync(IOutboxRepository repository, CancellationToken cancellationToken)
        {
            if (_options.RetentionDays <= 0) return;

            var cutoff = DateTime.UtcNow.AddDays(-_options.RetentionDays);
            var batchSize = Math.Max(1, _options.BatchSize);

            for (var i = 0; i < CleanupMaxBatches; i++)
            {
                if (!await repository.CleanupBatchAsync(cutoff, batchSize, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }

            // 不静默截断：这一轮没删完，下一轮接着删
            _logger.Info($"发件箱清理本轮已达 {CleanupMaxBatches} 批上限，剩余清理量留待下一轮");
        }

        /// <summary>
        /// 与 <c>VivWolverineConfigurationExtensions.GenerateExponentialBackoff</c> 同形：
        /// 5s 起、×2、封顶 60s、0~30% 抖动防惊群。
        /// </summary>
        internal static TimeSpan Backoff(int retryCount)
        {
            var exponent = Math.Max(0, retryCount - 1);
            var delay = Math.Min(BackoffBaseMilliseconds * Math.Pow(2, exponent), BackoffMaxMilliseconds);
            var jitter = RandomMagic.Next(0, (int)(delay * BackoffJitterRatio) + 1);
            return TimeSpan.FromMilliseconds(delay + jitter);
        }
    }
}
