using Microsoft.Extensions.DependencyInjection;

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

    public sealed class ProbeParams
    {
    }

    public sealed class ProbeHandler : VivAsyncTaskHandler<ProbeParams>
    {
        public override string TaskType => "probe";

        protected override Task ExecuteAsync(ProbeParams? request, IVivAsyncTaskContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;
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

        public bool CompleteResult { get; set; }

        public Task<bool> CompleteAsync(long id, string leaseOwner, string? resultJson, DateTime now, DateTime expireAt, CancellationToken cancellationToken = default)
        {
            CompleteCalls++;
            return Task.FromResult(CompleteResult);
        }

        public Task<bool> FailAsync(long id, string leaseOwner, VivAsyncTaskStatus status, string? errorMessage, DateTime? nextAttemptAt, DateTime now, DateTime? expireAt, CancellationToken cancellationToken = default)
        {
            FailCalls++;
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
            => Task.FromResult(new List<VivAsyncTask>());

        public Task<bool> ReleaseExpiredLeasesAsync(DateTime now, DateTime expireAt, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<bool> MarkCanceledAsync(long id, DateTime now, DateTime expireAt, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> ReportProgressAsync(long id, int progress, int? totalCount, int? doneCount, string? message, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> CleanupAsync(int batchSize, DateTime now, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }
}
