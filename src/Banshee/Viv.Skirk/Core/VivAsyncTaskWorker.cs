using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Log;
using Viv.Skirk.Options;

namespace Viv.Skirk.Core
{
    /// <summary>
    /// 任务消费者。轮询 → 原子认领 → 每个任务独立 DI 作用域执行 → 心跳续租/查取消 → 收尾。
    ///
    /// 多副本安全靠数据库认领（READPAST / SKIP LOCKED），不依赖 Redis 锁；
    /// 副本崩溃后过期租约会被别的副本回收。
    /// </summary>
    public sealed class VivAsyncTaskWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly VivAsyncTaskHandlerRegistry _registry;
        private readonly SkirkOption _option;
        private readonly ILoggerContract _logger;

        /// <summary>租约持有者标识：机器 + 进程 + 随机后缀，进程重启不会复用。列宽 64，这里截到 48。</summary>
        private readonly string _leaseOwner = CreateLeaseOwner(Environment.MachineName, Environment.ProcessId, Guid.NewGuid());

        public VivAsyncTaskWorker(
            IServiceScopeFactory scopeFactory,
            VivAsyncTaskHandlerRegistry registry,
            SkirkOption option,
            ILoggerContract logger)
        {
            _scopeFactory = scopeFactory;
            _registry = registry;
            _option = option;
            _logger = logger;
        }

        /// <summary>机器名很短时整段都放得下；超长才截断。直接 [..48] 在短字符串上会抛。</summary>
        internal static string CreateLeaseOwner(string machineName, int processId, Guid token)
        {
            var raw = $"{machineName}:{processId}:{token:N}";
            return raw.Length <= 48 ? raw : raw[..48];
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                _registry.Build(scope.ServiceProvider);
            }

            await EnsureTableAsync(stoppingToken).ConfigureAwait(false);

            _logger.Info("Skirk 任务消费者已启动：轮询 {0}s / 批量 {1} / 并发 {2} / 租约 {3}s",
                _option.PollIntervalSeconds, _option.BatchSize, _option.MaxParallelism, _option.LeaseSeconds);

            var poll = TimeSpan.FromSeconds(Math.Max(1, _option.PollIntervalSeconds));
            using var timer = new PeriodicTimer(poll);
            var lastCleanup = DateTime.MinValue;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false)) break;

                    await PollOnceAsync(stoppingToken).ConfigureAwait(false);

                    if (DateTime.UtcNow - lastCleanup > TimeSpan.FromHours(6))
                    {
                        await CleanupAsync(stoppingToken).ConfigureAwait(false);
                        lastCleanup = DateTime.UtcNow;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // 轮询本身出问题不能把消费者打死
                    _logger.Error("Skirk 轮询异常", ex);
                }
            }
        }

        private async Task EnsureTableAsync(CancellationToken cancellationToken)
        {
            if (!_option.AutoCreateTable) return;

            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IVivAsyncTaskStore>();
            await store.EnsureTableAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task PollOnceAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IVivAsyncTaskStore>();

            var now = DateTime.UtcNow;

            // 先把"执行方崩了、租约过期"的任务放回队列，再认领。
            // 重试次数已经用尽的标成失败，不再无限回到队列。
            await store.ReleaseExpiredLeasesAsync(
                now,
                now.AddDays(Math.Max(1, _option.RetentionDays)),
                cancellationToken).ConfigureAwait(false);

            var claimed = await store
                .ClaimBatchAsync(
                    _option.BatchSize,
                    _leaseOwner,
                    now,
                    now.AddSeconds(Math.Max(5, _option.LeaseSeconds)),
                    cancellationToken)
                .ConfigureAwait(false);

            if (claimed.Count == 0) return;

            await Parallel.ForEachAsync(
                claimed,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, _option.MaxParallelism),
                    CancellationToken = cancellationToken,
                },
                (task, ct) => new ValueTask(ExecuteOneAsync(task, ct))).ConfigureAwait(false);
        }

        internal Task RunOneAsync(VivAsyncTask task, CancellationToken cancellationToken)
            => ExecuteOneAsync(task, cancellationToken);

        private async Task ExecuteOneAsync(VivAsyncTask task, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var provider = scope.ServiceProvider;
            var store = provider.GetRequiredService<IVivAsyncTaskStore>();
            var accessor = provider.GetRequiredService<IVivContextAccessor>();

            // 把任务行里的身份快照塞回上下文：后台没有 HttpContext，不塞的话租户过滤器会整条放行
            accessor.Current = new VivContextContent
            {
                AppId = task.AppId ?? 0,
                SubjectId = task.SubjectId ?? 0,
                UserId = task.UserId ?? 0,
                TraceId = task.TraceId,
            };

            var context = new VivAsyncTaskContext(task.Id, store, _option, _logger);

            using var taskCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            taskCts.CancelAfter(TimeSpan.FromMinutes(Math.Max(1, _option.TaskTimeoutMinutes)));

            using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var heartbeat = HeartbeatAsync(task, store, context, taskCts, heartbeatCts.Token);

            try
            {
                var handlerType = _registry.ResolveHandlerType(task.TaskType, provider);
                if (handlerType is null)
                {
                    // 不认识的任务类型：直接终态失败，别占着队列反复重试
                    await store.FailAsync(
                        task.Id, _leaseOwner, VivAsyncTaskStatus.Failed,
                        $"未注册的任务类型：{task.TaskType}", null, DateTime.UtcNow,
                        DateTime.UtcNow.AddDays(Math.Max(1, _option.RetentionDays)), cancellationToken).ConfigureAwait(false);
                    return;
                }

                var handler = (IVivAsyncTaskHandler)provider.GetRequiredService(handlerType);
                var @params = DeserializePayload(task.ParamsJson, handler.ParamsType);

                await handler.ExecuteAsync(@params, context, taskCts.Token).ConfigureAwait(false);

                var finishedAt = DateTime.UtcNow;
                var wrote = await store.CompleteAsync(
                    task.Id, _leaseOwner, null, finishedAt,
                    finishedAt.AddDays(Math.Max(1, _option.RetentionDays)), cancellationToken).ConfigureAwait(false);
                if (!wrote)
                {
                    _logger.Warning("Skirk 任务 {0} 完成时租约已不属于本副本，结果未写回", task.Id);
                }
            }
            catch (OperationCanceledException) when (context.CanceledByUser)
            {
                var now = DateTime.UtcNow;
                await store.MarkCanceledAsync(task.Id, now, now.AddDays(Math.Max(1, _option.RetentionDays)), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await FailOrRetryAsync(task, store, "执行超时或被停止", CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error($"Skirk 任务 {task.Id}（{task.TaskType}）执行失败", ex);
                await FailOrRetryAsync(task, store, ex.Message, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                heartbeatCts.Cancel();
                try { await heartbeat.ConfigureAwait(false); } catch (OperationCanceledException) { }

                accessor.Current = null;
            }
        }

        /// <summary>心跳：续租 + 查取消。续不上（租约被别人拿走）就取消本地执行。</summary>
        private async Task HeartbeatAsync(
            VivAsyncTask task,
            IVivAsyncTaskStore store,
            VivAsyncTaskContext context,
            CancellationTokenSource taskCts,
            CancellationToken cancellationToken)
        {
            var renewEvery = TimeSpan.FromSeconds(Math.Max(5, _option.LeaseSeconds / 3));
            var cancelEvery = TimeSpan.FromMilliseconds(Math.Max(50, _option.CancelPollIntervalMs));
            var tick = cancelEvery < renewEvery ? cancelEvery : renewEvery;
            using var timer = new PeriodicTimer(tick);
            var nextRenewAt = DateTime.UtcNow + renewEvery;

            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    var canceled = await store.IsCancelRequestedAsync(task.Id, cancellationToken).ConfigureAwait(false);
                    if (canceled == true)
                    {
                        context.SetCanceledByUser(true);
                        taskCts.Cancel();
                        return;
                    }

                    if (DateTime.UtcNow < nextRenewAt) continue;

                    var renewed = await store
                        .RenewLeaseAsync(task.Id, _leaseOwner, DateTime.UtcNow.AddSeconds(Math.Max(5, _option.LeaseSeconds)), cancellationToken)
                        .ConfigureAwait(false);

                    if (!renewed)
                    {
                        _logger.Warning("Skirk 任务 {0} 租约已失效，放弃本次执行", task.Id);
                        taskCts.Cancel();
                        return;
                    }

                    nextRenewAt = DateTime.UtcNow + renewEvery;
                }
            }
            catch (OperationCanceledException)
            {
                // 正常收尾
            }
            catch (Exception ex)
            {
                // 续不上就停掉本地执行。否则租约过期后别的副本会把同一条再跑一遍。
                _logger.Warning("Skirk 任务 {0} 心跳异常：{1}", task.Id, ex.Message);
                taskCts.Cancel();
            }
        }

        private async Task FailOrRetryAsync(VivAsyncTask task, IVivAsyncTaskStore store, string error, CancellationToken cancellationToken)
        {
            // 认领时 Attempt 已经 +1
            var attempt = task.Attempt;
            var canRetry = attempt <= Math.Max(0, task.MaxAttempts);
            var now = DateTime.UtcNow;

            var status = canRetry ? VivAsyncTaskStatus.Pending : VivAsyncTaskStatus.Failed;
            var nextAttemptAt = canRetry ? now.AddSeconds(Math.Min(300, Math.Pow(2, attempt))) : (DateTime?)null;
            var expireAt = canRetry ? (DateTime?)null : now.AddDays(Math.Max(1, _option.RetentionDays));

            var wrote = await store.FailAsync(
                task.Id, _leaseOwner, status, Truncate(error, 2000), nextAttemptAt, now, expireAt, cancellationToken)
                .ConfigureAwait(false);
            if (!wrote)
            {
                _logger.Warning("Skirk 任务 {0} 收尾时租约已不属于本副本，失败状态未写回", task.Id);
            }
        }

        private async Task CleanupAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IVivAsyncTaskStore>();

            var batchSize = Math.Max(1, _option.BatchSize) * 50;
            var deleted = 0;

            // 分批删，避免一次锁太多行
            while (deleted < 10_000)
            {
                var affected = await store.CleanupAsync(batchSize, DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
                if (!affected) break;

                deleted += batchSize;
            }
        }

        /// <summary>参数原样反序列化成 handler 声明的类型；兼容性由业务自己负责</summary>
        private static object? DeserializePayload(string json, Type paramsType)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            return JsonSerializer.Deserialize(json, paramsType, VivAsyncTaskService.JsonOptions);
        }

        private static string Truncate(string value, int max)
            => value.Length <= max ? value : value[..max];
    }
}
