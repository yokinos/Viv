using Testcontainers.MsSql;
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
/// 容器集成测试的开关特性。判据与跳过文案都在 <see cref="Viv.Fakes.ContainerTests"/>，
/// 这里只是 xUnit 的薄壳（特性必须继承 <c>FactAttribute</c>，而 Viv.Fakes 不带 xunit）。
/// </summary>
internal sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!Viv.Fakes.ContainerTests.IsEnabled)
        {
            Skip = Viv.Fakes.ContainerTests.SkipReason;
        }
    }
}

/// <summary>
/// 真库夹具的最小契约。序号那组测试要覆盖两种方言（OUTPUT / RETURNING），
/// 靠它把「用哪个库」从测试方法里摘出去。
/// </summary>
public interface IEngineDatabaseFixture
{
    string ConnectionString { get; }

    DatabaseSourceType Source { get; }
}

public sealed class EnginePostgresFixture : IAsyncLifetime, IEngineDatabaseFixture
{
    public PostgreSqlContainer? Container { get; private set; }

    public string ConnectionString => Container?.GetConnectionString()
        ?? throw new InvalidOperationException("PostgreSQL 容器未启动");

    public DatabaseSourceType Source => DatabaseSourceType.PostgreSQL;

    public async Task InitializeAsync()
    {
        if (!Viv.Fakes.ContainerTests.IsEnabled) return;

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

public sealed class EngineSqlServerFixture : IAsyncLifetime, IEngineDatabaseFixture
{
    public MsSqlContainer? Container { get; private set; }

    public string ConnectionString => Container?.GetConnectionString()
        ?? throw new InvalidOperationException("SQL Server 容器未启动");

    public DatabaseSourceType Source => DatabaseSourceType.SqlServer;

    public async Task InitializeAsync()
    {
        if (!Viv.Fakes.ContainerTests.IsEnabled) return;

        Container = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithPassword("Viv_Sql_77!")
            .Build();
        await Container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (Container is not null)
            await Container.DisposeAsync();
    }
}

[CollectionDefinition("EngineSqlServer")]
public sealed class EngineSqlServerCollection : ICollectionFixture<EngineSqlServerFixture>;

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
        await uow.ExecuteAsync(async _ =>
        {
            Assert.True(await db.InsertAsync(new UowProbeRow { Id = committedId, Label = "ok" }));
            return true;
        });

        Assert.NotNull(await db.FindAsync<UowProbeRow>(committedId));

        var rolledId = 2L;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            uow.ExecuteAsync<int>(async _ =>
            {
                Assert.True(await db.InsertAsync(new UowProbeRow { Id = rolledId, Label = "nope" }));
                throw new InvalidOperationException("故意回滚");
            }));

        Assert.Null(await db.FindAsync<UowProbeRow>(rolledId));
    }

    private sealed class StaticOptions : IDatabaseOptionsProvider
    {
        private readonly DatabaseOptions _options;
        public StaticOptions(DatabaseOptions options) => _options = options;
        public DatabaseOptions GetRealOptions() => _options;
    }
}
