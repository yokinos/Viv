using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Viv.Contracts.Interface;
using Viv.Delusion.Generic;
using Viv.Fakes;
using Viv.Skirk.Core;
using Viv.Skirk.Options;

namespace Viv.Skirk.Tests;

public class VivAsyncTaskBehaviorTests
{
    [Fact]
    public void 短主机名_租约标识不抛且不超过列宽()
    {
        var owner = VivAsyncTaskWorker.CreateLeaseOwner("dev", 1, Guid.Parse("00112233445566778899aabbccddeeff"));

        Assert.StartsWith("dev:1:", owner);
        Assert.True(owner.Length <= 48);
    }

    [Fact]
    public void 超长主机名_租约标识截断到48()
    {
        var owner = VivAsyncTaskWorker.CreateLeaseOwner(new string('m', 80), 12345, Guid.NewGuid());

        Assert.Equal(48, owner.Length);
    }

    [Fact]
    public void 处理器按接口注册_注册表能解析到具体类型()
    {
        var services = new ServiceCollection();
        VivAsyncTaskRegister.AddHandler(services, typeof(ProbeHandler));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var registry = new VivAsyncTaskHandlerRegistry();
        var resolved = registry.ResolveHandlerType("probe", scope.ServiceProvider);

        Assert.Equal(typeof(ProbeHandler), resolved);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ProbeHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IVivAsyncTaskHandler>());
    }

    [Fact]
    public void TaskType重复_建表时抛()
    {
        var services = new ServiceCollection();
        VivAsyncTaskRegister.AddHandler(services, typeof(ProbeHandler));
        VivAsyncTaskRegister.AddHandler(services, typeof(DuplicateProbeHandler));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var registry = new VivAsyncTaskHandlerRegistry();

        var ex = Assert.Throws<InvalidOperationException>(() => registry.Build(scope.ServiceProvider));
        Assert.Contains("probe", ex.Message);
    }

    [Fact]
    public async Task 完成时影响行数为0_记租约丢失且不再改状态()
    {
        var store = new LeaseLossStore { CompleteResult = false };
        var services = new ServiceCollection();
        services.AddSingleton<IVivAsyncTaskStore>(store);
        services.AddSingleton<IVivContextAccessor>(new TestContextAccessor());
        VivAsyncTaskRegister.AddHandler(services, typeof(ProbeHandler));
        using var provider = services.BuildServiceProvider();

        var logger = new RecordingLogger();
        var worker = new VivAsyncTaskWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new VivAsyncTaskHandlerRegistry(),
            new SkirkOption
            {
                LeaseSeconds = 60,
                CancelPollIntervalMs = 60_000,
                TaskTimeoutMinutes = 1,
                RetentionDays = 1,
            },
            logger);

        await worker.RunOneAsync(new VivAsyncTask
        {
            Id = 42,
            TaskType = "probe",
            ParamsJson = "{}",
            Attempt = 1,
            MaxAttempts = 3,
        }, CancellationToken.None);

        Assert.Equal(1, store.CompleteCalls);
        Assert.Equal(0, store.FailCalls);
        Assert.Contains(logger.Warnings, message => message.Contains("租约已不属于本副本"));
    }

    [Fact]
    public async Task 外部叫停_任务归还队列且不计入重试()
    {
        // 认领时 Attempt 已经 +1。停机/调用方叫停时如果按「失败」记账，
        // MaxAttempts 小的任务一次发布就被推到 Failed —— 发布变成「烧任务」
        var store = new LeaseLossStore { CompleteResult = true };
        var worker = NewWorker(store, typeof(CanceledProbeHandler));

        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        await worker.RunOneAsync(new VivAsyncTask
        {
            Id = 7,
            TaskType = "canceled",
            ParamsJson = "{}",
            Attempt = 1,
            MaxAttempts = 1,
        }, stop.Token);

        Assert.Equal(1, store.ReleaseCalls);
        Assert.Equal(0, store.FailCalls);
    }

    [Fact]
    public async Task 执行超时_仍然按失败记账()
    {
        // 超时是真的没跑完，该退避重试，不能跟着「叫停」一起被免账。
        // 判据是「调用方的令牌有没有被取消」——没取消就只能是超时
        var store = new LeaseLossStore { CompleteResult = true };
        var worker = NewWorker(store, typeof(CanceledProbeHandler));

        await worker.RunOneAsync(new VivAsyncTask
        {
            Id = 8,
            TaskType = "canceled",
            ParamsJson = "{}",
            Attempt = 1,
            MaxAttempts = 3,
        }, CancellationToken.None);

        Assert.Equal(1, store.FailCalls);
        Assert.Equal(0, store.ReleaseCalls);
        Assert.Contains("执行超时", store.LastError);
    }

    /// <summary>
    /// 造一个只认一个 handler 的最小宿主。刻意不注入 FakeTimeProvider、不建阻塞 handler：
    /// 这两个用例要验的是「叫停/超时 怎么记账」，不是定时器本身 ——
    /// 用真定时器反而会把用例拖成几十秒，还会在测试主机退出时挂住。
    /// </summary>
    private static VivAsyncTaskWorker NewWorker(
        IVivAsyncTaskStore store,
        Type handlerType,
        object? handlerInstance = null,
        int pollSeconds = 0)
    {
        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddSingleton<IVivContextAccessor>(new TestContextAccessor());
        VivAsyncTaskRegister.AddHandler(services, handlerType);

        // 必须在 AddHandler 之后注册：后注册的赢，才能让 DI 解析到测试手里那个实例
        if (handlerInstance is not null)
        {
            services.AddSingleton(handlerInstance.GetType(), handlerInstance);
        }

        var provider = services.BuildServiceProvider();

        return new VivAsyncTaskWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new VivAsyncTaskHandlerRegistry(),
            new SkirkOption
            {
                LeaseSeconds = 60,
                CancelPollIntervalMs = 60_000,
                TaskTimeoutMinutes = 1,
                RetentionDays = 1,
                PollIntervalSeconds = pollSeconds,
            },
            new RecordingLogger());
    }

    public sealed class ProbeParams
    {
    }

    public sealed class ProbeHandler : VivAsyncTaskHandler<ProbeParams>
    {
        public override string TaskType => "probe";

        protected override Task ExecuteAsync(ProbeParams? request, IVivAsyncTaskContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    [Fact]
    public async Task 停机只停止认领_不掐在途任务()
    {
        // 这两个用例之外的第三个洞：Parallel.ForEachAsync 接了 stoppingToken 的话，
        // 停机时正在跑的任务全被取消 —— 表现是一次发布打断一批任务，白烧重试
        var store = new LeaseLossStore { CompleteResult = true };
        var handler = new GateProbeHandler();

        store.TaskToClaim = new VivAsyncTask
        {
            Id = 9,
            TaskType = "gate",
            ParamsJson = "{}",
            Attempt = 1,
            MaxAttempts = 3,
        };

        var worker = NewWorker(store, typeof(GateProbeHandler), handler, pollSeconds: 1);

        using var stop = new CancellationTokenSource();
        await worker.StartAsync(stop.Token);

        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await stop.CancelAsync();

        // 停机信号发出后等一拍：在途任务应该还在跑，没有被取消、也没有被归还
        await Task.Delay(50);

        Assert.False(handler.Canceled);
        Assert.Equal(0, store.ReleaseCalls);

        handler.Release();

        await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(handler.Completed);
        Assert.Equal(1, store.CompleteCalls);
    }

    /// <summary>等测试放行才返回，且**只认传进来的令牌**：令牌被取消就记一笔 Canceled。</summary>
    public sealed class GateProbeHandler : VivAsyncTaskHandler<ProbeParams>
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Canceled { get; private set; }

        public bool Completed { get; private set; }

        public override string TaskType => "gate";

        public void Release() => _release.TrySetResult();

        protected override async Task ExecuteAsync(
            ProbeParams? request,
            IVivAsyncTaskContext context,
            CancellationToken cancellationToken)
        {
            Entered.TrySetResult();

            try
            {
                await _release.Task.WaitAsync(cancellationToken);
                Completed = true;
            }
            catch (OperationCanceledException)
            {
                Canceled = true;
                throw;
            }
        }
    }

    /// <summary>一进 ExecuteAsync 就按「被取消」退出，用来验两条记账路径的分叉。</summary>
    public sealed class CanceledProbeHandler : VivAsyncTaskHandler<ProbeParams>
    {
        public override string TaskType => "canceled";

        protected override Task ExecuteAsync(
            ProbeParams? request,
            IVivAsyncTaskContext context,
            CancellationToken cancellationToken)
            => throw new OperationCanceledException(cancellationToken);
    }

    public sealed class DuplicateProbeHandler : VivAsyncTaskHandler<ProbeParams>
    {
        public override string TaskType => "probe";

        protected override Task ExecuteAsync(ProbeParams? request, IVivAsyncTaskContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class LeaseLossStore : IVivAsyncTaskStore
    {
        public int CompleteCalls { get; private set; }

        public int FailCalls { get; private set; }

        public int ReleaseCalls { get; private set; }

        /// <summary>最近一次 FailAsync 的错误信息，用来断言「超时」这条文案。</summary>
        public string? LastError { get; private set; }

        public bool CompleteResult { get; set; }

        /// <summary>非 null 时下一次 ClaimBatch 会把它交出去（只交一次）。</summary>
        public VivAsyncTask? TaskToClaim { get; set; }

        public int ClaimCalls { get; private set; }

        public Task<bool> CompleteAsync(long id, string leaseOwner, string? resultJson, DateTime now, DateTime expireAt, CancellationToken cancellationToken = default)
        {
            CompleteCalls++;
            return Task.FromResult(CompleteResult);
        }

        public Task<bool> FailAsync(long id, string leaseOwner, VivAsyncTaskStatus status, string? errorMessage, DateTime? nextAttemptAt, DateTime now, DateTime? expireAt, CancellationToken cancellationToken = default)
        {
            FailCalls++;
            LastError = errorMessage;
            return Task.FromResult(true);
        }

        public Task<bool> ReleaseAsync(long id, string leaseOwner, DateTime now, CancellationToken cancellationToken = default)
        {
            ReleaseCalls++;
            return Task.FromResult(true);
        }

        public Task<bool?> IsCancelRequestedAsync(long id, CancellationToken cancellationToken = default)
            => Task.FromResult<bool?>(false);

        public Task<bool> RenewLeaseAsync(long id, string leaseOwner, DateTime leaseUntil, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task EnsureTableAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<VivAsyncTask?> GetAsync(long id, CancellationToken cancellationToken = default)
            => Task.FromResult<VivAsyncTask?>(null);

        public Task<long?> FindIdByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
            => Task.FromResult<long?>(null);

        public Task<bool> InsertAsync(VivAsyncTask task, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<PagedList<VivAsyncTask>> QueryAsync(VivAsyncTaskQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(new PagedList<VivAsyncTask>(1, 1));

        public Task<bool> RequestCancelAsync(long id, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<List<VivAsyncTask>> ClaimBatchAsync(int batchSize, string leaseOwner, DateTime now, DateTime leaseUntil, CancellationToken cancellationToken = default)
        {
            ClaimCalls++;

            if (TaskToClaim is null)
            {
                return Task.FromResult(new List<VivAsyncTask>());
            }

            var claimed = TaskToClaim;
            TaskToClaim = null;

            return Task.FromResult(new List<VivAsyncTask> { claimed });
        }

        public Task<bool> ReleaseExpiredLeasesAsync(DateTime now, DateTime expireAt, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<bool> MarkCanceledAsync(long id, string leaseOwner, DateTime now, DateTime expireAt, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> ReportProgressAsync(long id, int progress, int? totalCount, int? doneCount, string? message, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> CleanupAsync(int batchSize, DateTime now, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }
}
