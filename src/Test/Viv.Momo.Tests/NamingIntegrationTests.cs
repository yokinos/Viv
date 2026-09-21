using Viv.Momo.Enums;
using Viv.Momo.Sync;

namespace Viv.Momo.Tests;

/// <summary>
/// 真库：SchemaSynchronizer 建表 → EF 插入/查询 → Dapper 批量打同一张表，
/// 再加 Momo 事务提交 / 回滚。Docker 不可用时 [DockerFact] 跳过。
/// </summary>
[Collection("PostgresContainer")]
public class PostgresNamingIntegrationTests
{
    private readonly PostgresContainerFixture _fx;

    public PostgresNamingIntegrationTests(PostgresContainerFixture fx) => _fx = fx;

    [DockerFact]
    public Task Sync后EF与Dapper打同一张表()
        => NamingIntegration.SyncEfDapperAsync(_fx.ConnectionString, DatabaseSourceType.PostgreSQL);

    [DockerFact]
    public Task 事务提交后能读到_回滚后读不到()
        => NamingIntegration.CommitAndRollbackAsync(_fx.ConnectionString, DatabaseSourceType.PostgreSQL);
}

[Collection("SqlServerContainer")]
public class SqlServerNamingIntegrationTests
{
    private readonly SqlServerContainerFixture _fx;

    public SqlServerNamingIntegrationTests(SqlServerContainerFixture fx) => _fx = fx;

    [DockerFact]
    public Task Sync后EF与Dapper打同一张表()
        => NamingIntegration.SyncEfDapperAsync(_fx.ConnectionString, DatabaseSourceType.SqlServer);

    [DockerFact]
    public Task 事务提交后能读到_回滚后读不到()
        => NamingIntegration.CommitAndRollbackAsync(_fx.ConnectionString, DatabaseSourceType.SqlServer);
}

internal static class NamingIntegration
{
    internal static async Task SyncEfDapperAsync(string connectionString, DatabaseSourceType source)
    {
        using var db = MomoTestDatabase.Open(connectionString, source);
        await db.SyncTableAsync();

        var physical = MomoIdentifier.ToPhysical(nameof(NamingProbeRow), source);
        var quoted = MomoIdentifier.Quote(physical, source);
        Assert.Equal(quoted, SqlMagic.GetTableName<NamingProbeRow>(source));

        var expected = new SchemaSynchronizer(MomoTestDatabase.Options(connectionString, source))
            .BuildExpectedSchema([typeof(NamingProbeRow)]);
        Assert.Equal(physical, expected[0].Name, StringComparer.OrdinalIgnoreCase);

        var efId = 1L;
        Assert.True(await db.InsertAsync(new NamingProbeRow
        {
            Id = efId,
            DisplayName = "ef",
            ItemCount = 1
        }));

        var fromEf = await db.FindAsync<NamingProbeRow>(efId);
        Assert.NotNull(fromEf);
        Assert.Equal("ef", fromEf!.DisplayName);

        // ≥ EFMaxCount(200) 走 Dapper 批量 INSERT，必须打到 EF 刚建的那张表
        var bulk = Enumerable.Range(2, 200)
            .Select(i => new NamingProbeRow { Id = i, DisplayName = $"dapper-{i}", ItemCount = i })
            .ToList();
        Assert.True(await db.InsertAsync(bulk));

        var fromDapper = await db.FindAsync<NamingProbeRow>(2);
        Assert.NotNull(fromDapper);
        Assert.Equal("dapper-2", fromDapper!.DisplayName);

        Assert.Equal(201, await db.CountAsync<NamingProbeRow>(_ => true));
    }

    internal static async Task CommitAndRollbackAsync(string connectionString, DatabaseSourceType source)
    {
        using var db = MomoTestDatabase.Open(connectionString, source);
        await db.SyncTableAsync();

        var committedId = 9001L;
        Assert.True(await db.BeginTransactionAsync());
        Assert.True(await db.InsertAsync(new NamingProbeRow
        {
            Id = committedId,
            DisplayName = "committed",
            ItemCount = 1
        }));
        await db.CommitTransactionAsync();
        Assert.NotNull(await db.FindAsync<NamingProbeRow>(committedId));

        var rolledId = 9002L;
        Assert.True(await db.BeginTransactionAsync());
        Assert.True(await db.InsertAsync(new NamingProbeRow
        {
            Id = rolledId,
            DisplayName = "rolled",
            ItemCount = 2
        }));
        await db.RollbackTransactionAsync();
        Assert.Null(await db.FindAsync<NamingProbeRow>(rolledId));
    }
}
