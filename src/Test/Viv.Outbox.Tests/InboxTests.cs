using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Viv.Contracts.Interface;
using Viv.Fakes;
using Viv.Momo.Enums;
using Viv.Outbox;
using Viv.Outbox.Core;

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
    }

    [Fact]
    public void 插入语句_走参数化()
    {
        Assert.Contains("@ServiceName", InboxSql.Insert);
        Assert.Contains("@MessageId", InboxSql.Insert);
        Assert.Contains("@AcceptedAt", InboxSql.Insert);
        Assert.StartsWith("INSERT INTO VivInboxMessage", InboxSql.Insert, StringComparison.OrdinalIgnoreCase);
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
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IHostedService));
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
