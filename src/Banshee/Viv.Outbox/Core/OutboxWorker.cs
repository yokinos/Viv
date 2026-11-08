using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using Viv.Delusion.Magic;
using Viv.Log;
using Viv.Nana;
using Viv.Outbox.Options;

namespace Viv.Outbox.Core
{
    /// <summary>
    /// 发件箱投递的一轮工作：起表 → 释放过期租约 → 排空认领并投递 → 清理。
    ///
    /// 与 <see cref="OutboxDispatcher"/> 分开是为了可测：调度那层（循环、睡眠、吞异常）没什么可测的，
    /// 认领 / 退避 / 置 Failed / 清理这些语义都在这里，可以脱离宿主和数据库直接跑。
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
        private readonly TimeProvider _time;

        public OutboxWorker(
            IServiceScopeFactory scopeFactory,
            IOutboxEnvelopeSenderFactory senderFactory,
            ILoggerContract logger,
            OutboxOptions options,
            TimeProvider? timeProvider = null)
        {
            _scopeFactory = scopeFactory;
            _senderFactory = senderFactory;
            _logger = logger;
            _options = options;
            _time = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// 本轮所有时间判断的唯一来源：认领时刻、租约到期、已发送时刻、退避、保留期都从这里取。
        /// 直接读 <c>DateTime.UtcNow</c> 会被 BannedSymbols 拦成编译错误 —— 不是洁癖：
        /// 这条链上的每个判断都要能在测试里把时钟拨过去验证，而拨系统时钟是不可能的。
        /// </summary>
        private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

        /// <summary>
        /// 建表并打一行启动日志。
        /// <returns>false = 起不来，投递器不该继续跑。</returns>
        /// </summary>
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
                // 这一次起不来。调度层隔一个轮询间隔再试，库恢复之后不用重启进程。
                _logger.Error("发件箱投递器启动失败，稍后重试 —— 这一轮还发不出去", ex);
                return false;
            }
        }

        /// <summary>
        /// 跑一轮。
        /// <returns>本轮认领并尝试投递的条数（测试与日志用）。
        /// </returns>
        /// </summary>
        public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();

            var repository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
            var publisher = scope.ServiceProvider.GetRequiredService<IVivEventPublisher>();

            // 1) 崩溃恢复：上次进程被 kill 时卡在 Processing 的行，租约一过就退回 Pending
            await repository.ReleaseExpiredLeasesAsync(UtcNow, cancellationToken).ConfigureAwait(false);

            var batchSize = Math.Max(1, _options.BatchSize);
            var delivered = 0;

            // 2) 排空：一直认领到认不出为止 —— 只看一批的话，积压时投递速度会被轮询间隔卡死
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = UtcNow;
                var leaseUntil = now.AddSeconds(Math.Max(1, _options.LeaseSeconds));

                var batch = await repository
                    .ClaimBatchAsync(batchSize, now, leaseUntil, cancellationToken)
                    .ConfigureAwait(false);

                if (batch.Count == 0) break;

                foreach (var message in batch)
                {
                    await DeliverAsync(repository, publisher, message, leaseUntil, cancellationToken).ConfigureAwait(false);
                }

                delivered += batch.Count;

                // 不满一批 = 已经认空了，不必再多跑一次往返
                if (batch.Count < batchSize) break;
            }

            // 3) 清理已投递且超过保留期的行
            await CleanupAsync(repository, cancellationToken).ConfigureAwait(false);

            try
            {
                var (pending, failed) = await repository.CountDepthAsync(cancellationToken).ConfigureAwait(false);
                OutboxMetrics.SetDepth(pending, failed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.Warning($"发件箱深度统计失败：{ex.Message}");
            }

            return delivered;
        }

        internal async Task DeliverAsync(
            IOutboxRepository repository, 
            IVivEventPublisher publisher, 
            OutboxMessage message,
            DateTime leaseUntil,
            CancellationToken cancellationToken = default)
        {
            var sender = _senderFactory.Resolve(message.EventType);
            if (sender is null)
            {
                // 事件类型解析不出来 = 这条消息永远发不出去。置 Failed 并记 Error，绝不静默丢：
                // 多半是 EventType 写错，或者事件类型所在程序集没被加载。
                _logger.Error(
                    $"发件箱消息的事件类型解析不出来，置为 Failed：EventType={message.EventType}, " +
                    $"MessageId={message.MessageId}, Id={message.Id}");
                await MarkTerminalFailureAsync(
                    repository, message, leaseUntil, message.RetryCount, $"未知事件类型：{message.EventType}", cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            try
            {
                var started = Stopwatch.GetTimestamp();
                var published = await sender.SendAsync(publisher, message.Payload, message.MessageId, cancellationToken)
                    .ConfigureAwait(false);
                if (!published)
                {
                    // 正文反序列化成空。重试不会让它长出内容，按未知类型同样直接 Failed。
                    _logger.Error(
                        $"发件箱消息正文为空，置为 Failed：EventType={message.EventType}, " +
                        $"MessageId={message.MessageId}, Id={message.Id}");
                    await MarkTerminalFailureAsync(
                        repository, message, leaseUntil, message.RetryCount, "投递内容为空", cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }

                var elapsed = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (await repository.MarkSentAsync(message.Id, UtcNow, leaseUntil, cancellationToken).ConfigureAwait(false))
                {
                    OutboxMetrics.RecordDelivered(elapsed);
                }
                else
                {
                    LogLeaseLost(message, "标记已发送");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 停机：不消耗重试次数。租约到期后这条自然会被重新认领。
                throw;
            }
            catch (Exception ex)
            {
                await HandleFailureAsync(repository, message, leaseUntil, ex, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 投递失败一律先重试 —— 不区分「MQ 挂了」和「序列化/路由问题」，
        /// 两类在这里的处理本来就是同一个：记 LastError + 退避重投，到上限才置 Failed。
        /// </summary>
        private async Task HandleFailureAsync(IOutboxRepository repository, OutboxMessage message, DateTime leaseUntil, Exception ex, CancellationToken cancellationToken)
        {
            var maxRetryCount = Math.Max(1, _options.MaxRetryCount);
            var retryCount = message.RetryCount + 1;

            if (retryCount >= maxRetryCount)
            {
                _logger.Error(
                    $"发件箱消息重试 {retryCount} 次仍失败，置为 Failed（等人工介入）：" +
                    $"MessageId={message.MessageId}, Id={message.Id}, EventType={message.EventType}",
                    ex);
                await MarkTerminalFailureAsync(repository, message, leaseUntil, retryCount, ex.Message, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            var nextRetryAt = UtcNow.Add(Backoff(retryCount));
            _logger.Warning(
                $"发件箱消息投递失败，第 {retryCount}/{maxRetryCount} 次重试将于 {nextRetryAt:yyyy-MM-dd HH:mm:ss} UTC 进行：" +
                $"MessageId={message.MessageId}, Id={message.Id}, {ex.Message}");
            if (!await repository
                .MarkPendingAsync(message.Id, retryCount, nextRetryAt, leaseUntil, ex.Message, cancellationToken)
                .ConfigureAwait(false))
            {
                LogLeaseLost(message, "退回待投");
            }
        }

        /// <summary>永久失败。不增加调用方已经算好的次数；租约丢了就不改行，避免盖住后来的认领。</summary>
        private async Task MarkTerminalFailureAsync(
            IOutboxRepository repository,
            OutboxMessage message,
            DateTime leaseUntil,
            int retryCount,
            string? lastError,
            CancellationToken cancellationToken)
        {
            if (await repository.MarkFailedAsync(message.Id, retryCount, leaseUntil, lastError, cancellationToken).ConfigureAwait(false))
            {
                OutboxMetrics.RecordFailed();
                return;
            }

            LogLeaseLost(message, "置为失败");
        }

        private void LogLeaseLost(OutboxMessage message, string action)
        {
            _logger.Warning(
                $"发件箱消息租约已不在本轮，跳过{action}：MessageId={message.MessageId}, Id={message.Id}, EventType={message.EventType}");
        }

        private async Task CleanupAsync(IOutboxRepository repository, CancellationToken cancellationToken)
        {
            if (_options.RetentionDays <= 0) return;

            var cutoff = UtcNow.AddDays(-_options.RetentionDays);
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
