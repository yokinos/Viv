using Testcontainers.PostgreSql;
using Viv.Engine.UnitOfWork;
using Viv.Fakes;
using Viv.Momo.Core;
using Viv.Momo.Enums;
using Viv.Momo.Interface;
using Viv.Momo.Options;
using Viv.Delusion.Magic;

namespace Viv.Engine.Tests;

/// <summary>
/// 显式开启才会跑：环境变量 VIV_CONTAINER_TESTS 为 1 或 true 时才拉起 Testcontainers，否则跳过。
/// 与 Viv.Momo.Tests 同名特性同一条约定，两处判据要保持一致。
/// </summary>
internal sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!DockerEnvironment.IsEnabled)
        {
            Skip = "容器集成测试未开启，跳过。要跑请设环境变量 VIV_CONTAINER_TESTS=1，并确保本机 Docker 可用。";
        }
    }
}

internal static class DockerEnvironment
{
    internal static bool IsEnabled { get; } = Detect();

    private static bool Detect()
        => Environment.GetEnvironmentVariable("VIV_CONTAINER_TESTS") is "1" or "true" or "TRUE";
}

public sealed class EnginePostgresFixture : IAsyncLifetime
{
    public PostgreSqlContainer? Container { get; private set; }

    public string ConnectionString => Container?.GetConnectionString()
        ?? throw new InvalidOperationException("PostgreSQL 容器未启动");

    public async Task InitializeAsync()
    {
        if (!DockerEnvironment.IsEnabled) return;

        Container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("viv_uow")
            .WithUsername("viv")
            .WithPassword("viv")
            .Build();
        await Container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (Container is not null)
            await Container.DisposeAsync();
    }
}

[CollectionDefinition("EnginePostgres")]
public sealed class EnginePostgresCollection : ICollectionFixture<EnginePostgresFixture>;

/// <summary>UoW 真库探测行。不以 Entity 结尾，避免和 Momo 单测扫描撞车。</summary>
public class UowProbeRow : IEntity
{
    public long Id { get; set; }
    public string Label { get; set; } = "";
}

[Collection("UnitOfWork")]
public class UnitOfWorkDatabaseTests : IClassFixture<EnginePostgresFixture>
{
    private readonly EnginePostgresFixture _fx;

    public UnitOfWorkDatabaseTests(EnginePostgresFixture fx) => _fx = fx;

    [DockerFact]
    public async Task 窄事务提交后能读到_未提交释放则回滚()
    {
        var options = new DatabaseOptions
        {
            DatabaseSource = DatabaseSourceType.PostgreSQL,
            MasterConnectionString = _fx.ConnectionString,
            Timeout = 30,
            EntityTypeOptions =
            [
                new FilterTypeOptions
                {
                    AssemblyName = "Viv.Engine.Tests",
                    Namespace = "Viv.Engine.Tests",
                    ClassNameEndsWith = "UowProbeRow",
                    BaseType = "Viv.Momo.Interface.IEntity, Viv.Momo",
                }
            ]
        };

        using var db = new MomoDatabaseContext(
            new TestContext(),
            new RecordingLogger(),
            new StaticOptions(options));
        await db.SyncTableAsync();

        var logger = new RecordingLogger();
        var uow = new UnitOfWorkManager(new MomoTransactionAdapter(db), logger);

        var committedId = 1L;
        await using (var tx = await uow.BeginAsync())
        {
            Assert.True(await db.InsertAsync(new UowProbeRow { Id = committedId, Label = "ok" }));
            await tx.CommitAsync();
        }

        Assert.NotNull(await db.FindAsync<UowProbeRow>(committedId));

        var rolledId = 2L;
        await using (var tx = await uow.BeginAsync())
        {
            Assert.True(await db.InsertAsync(new UowProbeRow { Id = rolledId, Label = "nope" }));
        }

        Assert.Null(await db.FindAsync<UowProbeRow>(rolledId));
    }

    private sealed class StaticOptions : IDatabaseOptionsProvider
    {
        private readonly DatabaseOptions _options;
        public StaticOptions(DatabaseOptions options) => _options = options;
        public DatabaseOptions GetRealOptions() => _options;
    }
}
