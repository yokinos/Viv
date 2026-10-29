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

        /// <summary>租约持有者标识：机器 + 进程 + 随机后缀，进程重启不会复用</summary>
        private readonly string _leaseOwner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}"[..48];

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

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
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

                    if (DateTime.Now - lastCleanup > TimeSpan.FromHours(6))
                    {
                        await CleanupAsync(stoppingToken).ConfigureAwait(false);
                        lastCleanup = DateTime.Now;
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
            var store = scope.ServiceProvider.GetRequiredService<VivAsyncTaskStore>();
            await store.EnsureTableAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task PollOnceAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<VivAsyncTaskStore>();

            var now = DateTime.Now;

            // 先把"执行方崩了、租约过期"的任务放回队列，再认领
            await store.ReleaseExpiredLeasesAsync(now, cancellationToken).ConfigureAwait(false);

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

        private async Task ExecuteOneAsync(VivAsyncTask task, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var provider = scope.ServiceProvider;
            var store = provider.GetRequiredService<VivAsyncTaskStore>();
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
                        $"未注册的任务类型：{task.TaskType}", null, DateTime.Now,
                        DateTime.Now.AddDays(Math.Max(1, _option.RetentionDays)), cancellationToken).ConfigureAwait(false);
                    return;
                }

                var handler = (IVivAsyncTaskHandler)provider.GetRequiredService(handlerType);
                var @params = DeserializePayload(task.ParamsJson, handler.ParamsType);

                await handler.ExecuteAsync(@params, context, taskCts.Token).ConfigureAwait(false);

                var finishedAt = DateTime.Now;
                await store.CompleteAsync(
                    task.Id, _leaseOwner, null, finishedAt,
                    finishedAt.AddDays(Math.Max(1, _option.RetentionDays)), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (context.CanceledByUser)
            {
                var now = DateTime.Now;
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
            VivAsyncTaskStore store,
            VivAsyncTaskContext context,
            CancellationTokenSource taskCts,
            CancellationToken cancellationToken)
        {
            var interval = TimeSpan.FromSeconds(Math.Max(5, _option.LeaseSeconds / 3));
            using var timer = new PeriodicTimer(interval);

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

                    var renewed = await store
                        .RenewLeaseAsync(task.Id, _leaseOwner, DateTime.Now.AddSeconds(Math.Max(5, _option.LeaseSeconds)), cancellationToken)
                        .ConfigureAwait(false);

                    if (!renewed)
                    {
                        _logger.Warning("Skirk 任务 {0} 租约已失效，放弃本次执行", task.Id);
                        taskCts.Cancel();
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常收尾
            }
            catch (Exception ex)
            {
                _logger.Warning("Skirk 任务 {0} 心跳异常：{1}", task.Id, ex.Message);
            }
        }

        private async Task FailOrRetryAsync(VivAsyncTask task, VivAsyncTaskStore store, string error, CancellationToken cancellationToken)
        {
            // 认领时 Attempt 已经 +1
            var attempt = task.Attempt;
            var canRetry = attempt <= Math.Max(0, task.MaxAttempts);
            var now = DateTime.Now;

            var status = canRetry ? VivAsyncTaskStatus.Pending : VivAsyncTaskStatus.Failed;
            var nextAttemptAt = canRetry ? now.AddSeconds(Math.Min(300, Math.Pow(2, attempt))) : (DateTime?)null;
            var expireAt = canRetry ? (DateTime?)null : now.AddDays(Math.Max(1, _option.RetentionDays));

            await store.FailAsync(
                task.Id, _leaseOwner, status, Truncate(error, 2000), nextAttemptAt, now, expireAt, cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task CleanupAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<VivAsyncTaskStore>();

            var batchSize = Math.Max(1, _option.BatchSize) * 50;
            var deleted = 0;

            // 分批删，避免一次锁太多行
            while (deleted < 10_000)
            {
                var affected = await store.CleanupAsync(batchSize, DateTime.Now, cancellationToken).ConfigureAwait(false);
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
