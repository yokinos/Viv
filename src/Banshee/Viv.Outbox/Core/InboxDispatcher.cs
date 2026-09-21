using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Viv.Log;
using Viv.Outbox.Options;

namespace Viv.Outbox.Core
{
    /// <summary>
    /// Inbox 清理器的后台宿主：删一轮、睡一会儿、再来一轮。
    ///
    /// 单独一条循环，而不是折进 <see cref="OutboxDispatcher"/> —— 两者的启用条件不同：
    /// Inbox 只要配了 DatabaseOption 就注册（见 <c>VivRegister.RegisterInbox</c>），而 OutboxOption 未必配
    /// （仓库里 6 个有库的服务只有 1 个配了）。折进去的话其余服务永远不会清理，而且是静默的，表只会涨。
    ///
    /// 与 <see cref="OutboxDispatcher"/> 同样是 Singleton（AddHostedService），不能构造注入 Scoped 服务，
    /// 每轮自己开作用域。配置直接注入 <see cref="InboxOptions"/> 而不是 IOptions —— 该节点允许缺席，
    /// 用 IOptions 的话节点没配时容器解析不到，会把宿主直接拖垮。
    /// </summary>
    internal sealed class InboxDispatcher : BackgroundService
    {
        /// <summary>单轮最多删几批 —— 防的不是积压，是保留期配小了之后整轮耗在清理上。</summary>
        private const int CleanupMaxBatches = 50;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILoggerContract _logger;
        private readonly InboxOptions _options;

        public InboxDispatcher(IServiceScopeFactory scopeFactory, ILoggerContract logger, InboxOptions options)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _options = options;
        }

        private bool Enabled => _options.RetentionDays > 0;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!Enabled)
            {
                _logger.Info("Inbox 清理未启用（保留期 <= 0），VivInboxMessage 不会被自动清理");
                return;
            }

            var interval = TimeSpan.FromMinutes(Math.Max(1, _options.CleanupIntervalMinutes));
            _logger.Info(
                $"Inbox 清理器已启动：间隔 {interval.TotalMinutes:0.#} 分钟 / " +
                $"保留 {_options.RetentionDays} 天 / 批 {Math.Max(1, _options.BatchSize)}");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var batches = await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                    if (batches > 0)
                    {
                        _logger.Info($"Inbox 本轮清理 {batches} 批");
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // 后台服务里逃出去的异常会直接停掉整个宿主，清理失败不能拖垮业务进程
                    _logger.Error("Inbox 清理轮次异常（已吞掉，不影响宿主机）", ex);
                }

                try
                {
                    await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>跑一轮清理。<returns>本轮删掉的批数（测试与日志用）。</returns></summary>
        internal async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
        {
            if (!Enabled) return 0;

            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IInboxRepository>();

            var cutoff = DateTime.UtcNow.AddDays(-_options.RetentionDays);
            var batchSize = Math.Max(1, _options.BatchSize);
            var batches = 0;

            for (var i = 0; i < CleanupMaxBatches; i++)
            {
                if (!await repository.CleanupBatchAsync(cutoff, batchSize, cancellationToken).ConfigureAwait(false))
                {
                    return batches;
                }

                batches++;
            }

            // 不静默截断：这一轮没删完，下一轮接着删
            _logger.Info($"Inbox 清理本轮已达 {CleanupMaxBatches} 批上限，剩余清理量留待下一轮");
            return batches;
        }
    }
}
