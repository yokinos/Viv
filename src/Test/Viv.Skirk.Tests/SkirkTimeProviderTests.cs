using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Viv.Contracts.Interface;
using Viv.Delusion.Generic;
using Viv.Fakes;
using Viv.Skirk.Core;
using Viv.Skirk.Options;

namespace Viv.Skirk.Tests;

/// <summary>
/// 时间来源迁移（墙钟 → <see cref="TimeProvider"/>）。
///
/// 这里只验证「时刻确实来自注入的时钟」以及「两个后台循环能被拨表驱动」，
/// 不重复断言既有业务语义（那些在 <c>VivAsyncTaskBehaviorTests</c> 里）。
/// </summary>
public class SkirkTimeProviderTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public async Task 入队时刻与下次可认领时刻都来自注入时钟()
    {
        var store = new RecordingStore();
        var time = new FakeTimeProvider(Start);
        var service = new VivAsyncTaskService(
            store, new RecordingLogger(), new SkirkOption { AutoCreateTable = false }, new TestContext(), time);

        await service.EnqueueAsync("probe", new SkirkTimeParams());

        var task = Assert.IsType<VivAsyncTask>(store.LastInserted);
        Assert.Equal(Start.UtcDateTime, task.CreatedAt);
        Assert.Equal(Start.UtcDateTime, task.NextAttemptAt);

        // Kind 必须是 Utc：PG 的 TIMESTAMPTZ 拒绝 Unspecified，这个 bug 只有真连库才暴露
        Assert.Equal(DateTimeKind.Utc, task.CreatedAt.Kind);
    }

    [Fact]
    public async Task 进度节流按注入时钟放行()
    {
        var store = new RecordingStore();
        var time = new FakeTimeProvider(Start);
        var context = new VivAsyncTaskContext(
            1, store, new SkirkOption { ProgressMinIntervalMs = 1000 }, new RecordingLogger(), time);

        await context.ReportAsync(10);
        await context.ReportAsync(20);

        // 同一时刻的第二次上报被节流挡住
        Assert.Equal(1, store.ProgressCalls);

        time.Advance(TimeSpan.FromSeconds(1));
        await context.ReportAsync(30);

        Assert.Equal(2, store.ProgressCalls);
        Assert.Equal(30, store.LastProgress);
    }

    [Fact]
    public async Task 轮询循环挂载在注入时钟上_不推时钟一轮都不跑()
    {
        // PeriodicTimer 时代这条测不了：它不认 TimeProvider，只能真等一个轮询间隔。
        var store = new RecordingStore();
        var time = new FakeTimeProvider(Start);
        using var provider = BuildProvider(store);

        var worker = BuildWorker(provider, time, new SkirkOption { PollIntervalSeconds = 2, AutoCreateTable = false });

        await worker.StartAsync(CancellationToken.None);
        try
        {
            Assert.Equal(0, store.ClaimCalls);

            Assert.True(
                await AdvanceUntilAsync(time, () => store.ClaimCalls > 0, TimeSpan.FromSeconds(2)),
                "推进时钟后没有触发轮询");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task 保留期清理六小时一次_阈值来自注入时钟()
    {
        var store = new RecordingStore();
        var time = new FakeTimeProvider(Start);
        using var provider = BuildProvider(store);

        // 轮询间隔拉成一小时，方便按整小时推进。首轮必然清理（lastCleanup 初值是 MinValue）。
        var worker = BuildWorker(provider, time, new SkirkOption { PollIntervalSeconds = 3600, AutoCreateTable = false });

        await worker.StartAsync(CancellationToken.None);
        try
        {
            Assert.True(
                await AdvanceUntilAsync(time, () => store.CleanupCalls >= 1, TimeSpan.FromHours(1)),
                "首轮没有清理");
            var firstRound = store.CleanupCalls;

            // 再推 6 小时：距上次清理还不满 6 小时，一次都不该再清
            for (var i = 0; i < 6; i++)
            {
                time.Advance(TimeSpan.FromHours(1));
                await Task.Delay(20);
            }

            Assert.Equal(firstRound, store.CleanupCalls);

            // 满 6 小时之后才该再来一轮
            Assert.True(
                await AdvanceUntilAsync(time, () => store.CleanupCalls > firstRound, TimeSpan.FromHours(1)),
                "跨过 6 小时门槛后没有再次清理");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task 心跳按注入时钟续租_续租截止时刻从注入时刻起算()
    {
        var store = new RecordingStore();
        var time = new FakeTimeProvider(Start);
        var gate = new BlockingGate();
        using var provider = BuildProvider(store, services =>
        {
            services.AddSingleton(gate);
            VivAsyncTaskRegister.AddHandler(services, typeof(BlockingHandler));
        });

        var worker = BuildWorker(provider, time, new SkirkOption
        {
            LeaseSeconds = 30,
            CancelPollIntervalMs = 500,
            TaskTimeoutMinutes = 10,
            AutoCreateTable = false,
        });

        var run = worker.RunOneAsync(
            new VivAsyncTask { Id = 7, TaskType = "blocking", ParamsJson = "{}", Attempt = 1, MaxAttempts = 3 },
            CancellationToken.None);

        try
        {
            // 续租点 = 起点 + LeaseSeconds/3（10 秒）；把时钟拨到那里之前不该续
            Assert.True(
                await AdvanceUntilAsync(time, () => store.RenewCalls > 0, TimeSpan.FromMilliseconds(500)),
                "推进时钟后没有续租");

            // 续租截止 = 续租时刻（Start+10s）+ LeaseSeconds（30s）
            Assert.Equal(Start.UtcDateTime.AddSeconds(40), store.LastLeaseUntil);
        }
        finally
        {
            gate.Release.TrySetResult();
            await run;
        }
    }

    [Fact]
    public async Task 执行超时也挂注入时钟_推进到超时点即进重试分支()
    {
        var store = new RecordingStore();
        var time = new FakeTimeProvider(Start);
        var gate = new BlockingGate();
        using var provider = BuildProvider(store, services =>
        {
            services.AddSingleton(gate);
            VivAsyncTaskRegister.AddHandler(services, typeof(BlockingHandler));
        });

        var worker = BuildWorker(provider, time, new SkirkOption
        {
            LeaseSeconds = 600,
            CancelPollIntervalMs = 600_000,
            TaskTimeoutMinutes = 1,
            RetentionDays = 1,
            AutoCreateTable = false,
        });

        // handler 一直挂着，只有超时能把它停掉
        var run = worker.RunOneAsync(
            new VivAsyncTask { Id = 8, TaskType = "blocking", ParamsJson = "{}", Attempt = 1, MaxAttempts = 3 },
            CancellationToken.None);

        try
        {
            // 超时点（1 分钟）由假时钟决定：拨到那里之前 handler 该一直挂着
            Assert.True(
                await AdvanceUntilAsync(time, () => store.FailCalls > 0, TimeSpan.FromSeconds(10)),
                "推进到超时点后应走失败/重试收尾");
        }
        finally
        {
            gate.Release.TrySetResult();
            await run;
        }
    }

    private static ServiceProvider BuildProvider(RecordingStore store, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IVivAsyncTaskStore>(store);
        services.AddSingleton<IVivContextAccessor>(new TestContextAccessor());
        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }

    private static VivAsyncTaskWorker BuildWorker(ServiceProvider provider, TimeProvider time, SkirkOption option)
        => new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new VivAsyncTaskHandlerRegistry(),
            option,
            new RecordingLogger(),
            time);

    private static async Task<bool> AdvanceUntilAsync(FakeTimeProvider time, Func<bool> condition, TimeSpan step)
    {
        for (var i = 0; i < 200; i++)
        {
            if (condition()) return true;

            time.Advance(step);
            await Task.Delay(10);
        }

        return condition();
    }

    public sealed class SkirkTimeParams
    {
    }

    public sealed class BlockingGate
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>一直挂到测试放行或取消，用来观察心跳/超时。</summary>
    public sealed class BlockingHandler : VivAsyncTaskHandler<SkirkTimeParams>
    {
        private readonly BlockingGate _gate;

        public BlockingHandler(BlockingGate gate) => _gate = gate;

        public override string TaskType => "blocking";

        protected override Task ExecuteAsync(SkirkTimeParams? request, IVivAsyncTaskContext context, CancellationToken cancellationToken)
            => _gate.Release.Task.WaitAsync(cancellationToken);
    }

    /// <summary>记录被调用次数与最后一次入参，替代真实存储。</summary>
    private sealed class RecordingStore : IVivAsyncTaskStore
    {
        public VivAsyncTask? LastInserted { get; private set; }

        public int ClaimCalls { get; private set; }

        public int CleanupCalls { get; private set; }

        public int RenewCalls { get; private set; }

        public int ProgressCalls { get; private set; }

        public int FailCalls { get; private set; }

        public DateTime LastLeaseUntil { get; private set; }

        public int LastProgress { get; private set; }

        public Task<bool> InsertAsync(VivAsyncTask task, CancellationToken cancellationToken = default)
        {
            LastInserted = task;
            return Task.FromResult(true);
        }

        public Task<List<VivAsyncTask>> ClaimBatchAsync(int batchSize, string leaseOwner, DateTime now, DateTime leaseUntil, CancellationToken cancellationToken = default)
        {
            ClaimCalls++;
            return Task.FromResult(new List<VivAsyncTask>());
        }

        public Task<bool> CleanupAsync(int batchSize, DateTime now, CancellationToken cancellationToken = default)
        {
            CleanupCalls++;
            return Task.FromResult(false);
        }

        public Task<bool> RenewLeaseAsync(long id, string leaseOwner, DateTime leaseUntil, CancellationToken cancellationToken = default)
        {
            RenewCalls++;
            LastLeaseUntil = leaseUntil;
            return Task.FromResult(true);
        }

        public Task<bool> ReportProgressAsync(long id, int progress, int? totalCount, int? doneCount, string? message, CancellationToken cancellationToken = default)
        {
            ProgressCalls++;
            LastProgress = progress;
            return Task.FromResult(true);
        }

        public Task<bool> FailAsync(long id, string leaseOwner, VivAsyncTaskStatus status, string? errorMessage, DateTime? nextAttemptAt, DateTime now, DateTime? expireAt, CancellationToken cancellationToken = default)
        {
            FailCalls++;
            return Task.FromResult(true);
        }

        public Task<bool> CompleteAsync(long id, string leaseOwner, string? resultJson, DateTime now, DateTime expireAt, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool?> IsCancelRequestedAsync(long id, CancellationToken cancellationToken = default)
            => Task.FromResult<bool?>(false);

        public Task EnsureTableAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<VivAsyncTask?> GetAsync(long id, CancellationToken cancellationToken = default)
            => Task.FromResult<VivAsyncTask?>(null);

        public Task<long?> FindIdByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
            => Task.FromResult<long?>(null);

        public Task<PagedList<VivAsyncTask>> QueryAsync(VivAsyncTaskQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(new PagedList<VivAsyncTask>(1, 1));

        public Task<bool> RequestCancelAsync(long id, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<bool> ReleaseExpiredLeasesAsync(DateTime now, DateTime expireAt, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<bool> MarkCanceledAsync(long id, DateTime now, DateTime expireAt, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}
