using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Viv.Delusion.Magic;
using Viv.Fakes;
using Viv.Momo.Core;
using Viv.Momo.Enums;
using Viv.Momo.Interface;
using Viv.Momo.Options;

namespace Viv.Momo.Tests;

public sealed class PostgresContainerFixture : IAsyncLifetime
{
    public PostgreSqlContainer? Container { get; private set; }

    public string ConnectionString => Container?.GetConnectionString()
        ?? throw new InvalidOperationException("PostgreSQL 容器未启动");

    public async Task InitializeAsync()
    {
        if (!DockerEnvironment.IsAvailable) return;

        Container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("viv_momo")
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

public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    public MsSqlContainer? Container { get; private set; }

    public string ConnectionString => Container?.GetConnectionString()
        ?? throw new InvalidOperationException("SQL Server 容器未启动");

    public async Task InitializeAsync()
    {
        if (!DockerEnvironment.IsAvailable) return;

        Container = new MsSqlBuilder()
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

[CollectionDefinition("PostgresContainer")]
public sealed class PostgresContainerCollection : ICollectionFixture<PostgresContainerFixture>;

[CollectionDefinition("SqlServerContainer")]
public sealed class SqlServerContainerCollection : ICollectionFixture<SqlServerContainerFixture>;

internal sealed class StaticDatabaseOptionsProvider : IDatabaseOptionsProvider
{
    private readonly DatabaseOptions _options;
    public StaticDatabaseOptionsProvider(DatabaseOptions options) => _options = options;
    public DatabaseOptions GetRealOptions() => _options;
}

internal static class MomoTestDatabase
{
    internal static DatabaseOptions Options(string connectionString, DatabaseSourceType source)
        => new()
        {
            DatabaseSource = source,
            MasterConnectionString = connectionString,
            Timeout = 30,
            EntityTypeOptions =
            [
                new FilterTypeOptions
                {
                    AssemblyName = "Viv.Momo.Tests",
                    Namespace = "Viv.Momo.Tests",
                    ClassNameEndsWith = "NamingProbeRow",
                    BaseType = "Viv.Momo.Interface.IEntity, Viv.Momo",
                }
            ]
        };

    internal static MomoDatabaseContext Open(string connectionString, DatabaseSourceType source)
        => new(
            new TestContext(),
            new RecordingLogger(),
            new StaticDatabaseOptionsProvider(Options(connectionString, source)));
}
