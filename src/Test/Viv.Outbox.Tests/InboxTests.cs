using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Viv.Contracts.Interface;
using Viv.Fakes;
using Viv.Momo.Enums;
using Viv.Outbox;
using Viv.Outbox.Core;
using Viv.Outbox.Options;

namespace Viv.Outbox.Tests;

public class InboxSqlTests
{
    public static TheoryData<DatabaseSourceType> Sources =>
        new() { DatabaseSourceType.SqlServer, DatabaseSourceType.PostgreSQL };

    [Theory]
    [MemberData(nameof(Sources))]
    public void 建表脚本_幂等且主键是ServiceName加MessageId(DatabaseSourceType source)
    {
        var ddl = InboxSql.CreateTable(source);

        Assert.Contains("VivInboxMessage", ddl);
        Assert.Contains("ServiceName", ddl);
        Assert.Contains("MessageId", ddl);
        Assert.Contains("AcceptedAt", ddl);
        Assert.Contains("PRIMARY KEY", ddl, StringComparison.OrdinalIgnoreCase);

        var idempotent = ddl.Contains("IF OBJECT_ID", StringComparison.OrdinalIgnoreCase)
                         || ddl.Contains("IF NOT EXISTS", StringComparison.OrdinalIgnoreCase);
        Assert.True(idempotent, "Inbox 建表脚本没有幂等守卫");
        Assert.DoesNotContain("\"VivInboxMessage\"", ddl);
        Assert.DoesNotContain("[VivInboxMessage]", ddl);

        // 清理按 AcceptedAt 圈行，索引得跟着一起建 —— 主键是复合键，帮不上忙
        Assert.Contains("IX_VivInboxMessage_AcceptedAt", ddl);
    }

    [Fact]
    public void 插入语句_走参数化()
    {
        Assert.Contains("@ServiceName", InboxSql.Insert);
        Assert.Contains("@MessageId", InboxSql.Insert);
        Assert.Contains("@AcceptedAt", InboxSql.Insert);
        Assert.StartsWith("INSERT INTO VivInboxMessage", InboxSql.Insert, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public void 清理语句_按截止时间圈行且分批(DatabaseSourceType source)
    {
        var sql = InboxSql.CleanupBatch(source);

        Assert.StartsWith("DELETE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VivInboxMessage", sql);
        Assert.Contains("@Cutoff", sql);
        Assert.Contains("@BatchSize", sql);

        // 表是复合主键，没有单列 Id 可以照抄发件箱那条「Id IN (SELECT ...)」，两端各自分批：
        // SQL Server 有 DELETE TOP，PostgreSQL 没有 DELETE LIMIT，只能行值 IN 子查询。
        if (source == DatabaseSourceType.PostgreSQL)
        {
            Assert.Contains("(ServiceName, MessageId) IN", sql);
            Assert.Contains("LIMIT @BatchSize", sql);
        }
        else
        {
            Assert.Contains("DELETE TOP (@BatchSize)", sql);
        }
    }
}

public class InboxRegisterTests
{
    [Fact]
    public void Inbox与仓储都是Scoped()
    {
        var services = new ServiceCollection();
        InboxRegister.Initialize(services);

        Assert.Equal(ServiceLifetime.Scoped, services.Single(x => x.ServiceType == typeof(IVivInbox)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, services.Single(x => x.ServiceType == typeof(IInboxRepository)).Lifetime);
    }

    [Fact]
    public void 清理器随Inbox一起挂上()
    {
        var services = new ServiceCollection();
        InboxRegister.Initialize(services);

        Assert.Contains(services, x => x.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void 配置节点缺席时用默认值_而不是解析不到()
    {
        // Inbox 的启用条件是「配了数据库」，不要求 InboxOption 节点存在。
        // 用 IOptions<InboxOptions> 的话节点没配就解析不到，会把宿主直接拖垮 —— 这条钉住那个取舍。
        var services = new ServiceCollection();
        InboxRegister.Initialize(services, options: null);

        var descriptor = Assert.Single(services.Where(x => x.ServiceType == typeof(InboxOptions)));
        var options = Assert.IsType<InboxOptions>(descriptor.ImplementationInstance);
        Assert.Equal(7, options.RetentionDays);
    }

    [Fact]
    public void 传入的配置原样注册()
    {
        var services = new ServiceCollection();
        var configured = new InboxOptions { RetentionDays = 30 };
        InboxRegister.Initialize(services, configured);

        var descriptor = Assert.Single(services.Where(x => x.ServiceType == typeof(InboxOptions)));
        Assert.Same(configured, descriptor.ImplementationInstance);
    }
}

public class InboxDispatcherTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly StubInboxRepository _repository = new();

    public InboxDispatcherTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IInboxRepository>(_repository);
        _provider = services.BuildServiceProvider();
    }

    public void Dispose() => _provider.Dispose();

    private InboxDispatcher Build(RecordingLogger logger, InboxOptions options)
        => new(_provider.GetRequiredService<IServiceScopeFactory>(), logger, options);

    [Fact]
    public async Task 保留期不为正_一条都不删()
    {
        var dispatcher = Build(new RecordingLogger(), new InboxOptions { RetentionDays = 0 });

        Assert.Equal(0, await dispatcher.RunOnceAsync());
        Assert.Equal(0, _repository.CleanupCalls);
    }

    [Fact]
    public async Task 删不动就收工()
    {
        var dispatcher = Build(new RecordingLogger(), new InboxOptions());
        _repository.CleanupResult = false;

        Assert.Equal(0, await dispatcher.RunOnceAsync());
        Assert.Equal(1, _repository.CleanupCalls);
    }

    [Fact]
    public async Task 一直删到没得删为止()
    {
        var dispatcher = Build(new RecordingLogger(), new InboxOptions());
        _repository.CleanupScript.Enqueue(true);
        _repository.CleanupScript.Enqueue(true);
        _repository.CleanupScript.Enqueue(false);

        Assert.Equal(2, await dispatcher.RunOnceAsync());
        Assert.Equal(3, _repository.CleanupCalls);
    }

    [Fact]
    public async Task 达到批数上限就停_并且不静默截断()
    {
        var logger = new RecordingLogger();
        var dispatcher = Build(logger, new InboxOptions());
        _repository.CleanupResult = true;

        Assert.Equal(50, await dispatcher.RunOnceAsync());
        Assert.Equal(50, _repository.CleanupCalls);
        Assert.Contains(logger.Infos, x => x.Contains("上限"));
    }

    [Fact]
    public async Task 截止时间与批大小按配置算()
    {
        var dispatcher = Build(
            new RecordingLogger(),
            new InboxOptions { RetentionDays = 3, BatchSize = 250 });
        _repository.CleanupResult = false;

        var before = DateTime.UtcNow.AddDays(-3);
        await dispatcher.RunOnceAsync();
        var after = DateTime.UtcNow.AddDays(-3);

        Assert.Equal(250, _repository.LastCleanupBatchSize);
        Assert.NotNull(_repository.LastCleanupCutoff);
        Assert.InRange(_repository.LastCleanupCutoff!.Value, before.AddSeconds(-1), after.AddSeconds(1));
    }

    [Fact]
    public async Task 批大小配成0也至少删一行()
    {
        var dispatcher = Build(new RecordingLogger(), new InboxOptions { BatchSize = 0 });
        _repository.CleanupResult = false;

        await dispatcher.RunOnceAsync();

        Assert.Equal(1, _repository.LastCleanupBatchSize);
    }
}

public class InboxStoreTests
{
    [Fact]
    public async Task 首次接受返回true_重复返回false()
    {
        var repo = new StubInboxRepository();
        var store = new InboxStore(repo, new RecordingLogger());

        Assert.True(await store.TryAcceptAsync(42));
        Assert.False(await store.TryAcceptAsync(42));
        Assert.True(await store.TryAcceptAsync(43));
    }

    [Fact]
    public async Task MessageId为0返回false()
    {
        var store = new InboxStore(new StubInboxRepository(), new RecordingLogger());
        Assert.False(await store.TryAcceptAsync(0));
    }
}

public class OutboxMetricsTests
{
    [Fact]
    public void 仪器名稳定()
    {
        Assert.Equal("Viv.Outbox", OutboxMetrics.MeterName);
        Assert.Equal("viv.outbox.pending", OutboxMetrics.PendingGauge);
        Assert.Equal("viv.outbox.failed_depth", OutboxMetrics.FailedGauge);
        Assert.Equal("viv.outbox.publish.duration", OutboxMetrics.PublishDurationInstrument);
    }

    [Fact]
    public void RecordEnqueue能被MeterListener看到()
    {
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        var seen = 0L;
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OutboxMetrics.MeterName
                && instrument.Name == OutboxMetrics.EnqueuedInstrument)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => seen += value);
        listener.Start();

        OutboxMetrics.RecordEnqueue();
        listener.RecordObservableInstruments();

        Assert.True(seen >= 1);
    }
}
