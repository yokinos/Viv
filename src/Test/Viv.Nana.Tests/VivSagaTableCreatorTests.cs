using System;
using System.Collections.Generic;
using System.Linq;
using Viv.Momo.Enums;
using Viv.Momo.Options;
using Viv.Momo.Sync;
using Viv.Nana.Saga;
using Wolverine.Persistence.Sagas;

namespace Viv.Nana.Tests;

/// <summary>
/// Saga 建表的纯字符串断言：不连数据库，只验证「saga 类型 → 表结构 → DDL」这段推导。
///
/// 真正连库执行那步由启动钩子负责，这里钉的是两处最容易错的地方：
/// 表名必须和 VivSagaDbContext 的 ToTable 一致（Saga_{类名}），主键必须取 [SagaIdentity] 那一列
/// —— SchemaSynchronizer 默认只认 [Key] 或名字叫 Id 的属性，不看它就会建成一张没有主键的表。
/// </summary>
public class VivSagaTableCreatorTests
{
    private static readonly SchemaSynchronizer Sql = NewSynchronizer(DatabaseSourceType.SqlServer);

    private static readonly SchemaSynchronizer Pg = NewSynchronizer(DatabaseSourceType.PostgreSQL);

    private static SchemaSynchronizer NewSynchronizer(DatabaseSourceType source)
        => new(new DatabaseOptions { DatabaseSource = source, MasterConnectionString = "server=unused" });

    /// <summary>
    /// 只验证推导，连接串不会真的被用上。
    /// 注意：测试程序集里**不能**放没有 [SagaIdentity] 的 saga 类型 ——
    /// TypeScanMagic 会扫到它，集成测试里 EnsureTablesAsync 的全局扫描会跟着炸。
    /// </summary>
    public class TestSaga : VivSagaState
    {
        [SagaIdentity]
        public Guid OrderId { get; set; }

        public decimal Amount { get; set; }

        public string? Note { get; set; }
    }

    [Fact]
    public void 表名是Saga加类名_主键取SagaIdentity标记的列()
    {
        var tables = VivSagaTableCreator.BuildExpectedTables([typeof(TestSaga)], DatabaseSourceType.SqlServer, Sql);

        var table = Assert.Single(tables);
        Assert.Equal("Saga_TestSaga", table.Name);

        var primaryKeys = table.Columns.Where(c => c.IsPrimaryKey).ToList();
        var pk = Assert.Single(primaryKeys);
        Assert.Equal("OrderId", pk.Name);

        Assert.Contains(table.Columns, c => c.Name == "Amount");
        Assert.Contains(table.Columns, c => c.Name == "Note");
    }

    [Fact]
    public void 基类的AppId与SubjectId进了表()
    {
        // 这两个是 VivSagaState 上定义的，用来隔离租户/应用 —— 漏了它们等于 saga 表没有租户维度
        var table = Assert.Single(VivSagaTableCreator.BuildExpectedTables([typeof(TestSaga)], DatabaseSourceType.SqlServer, Sql));

        Assert.Contains(table.Columns, c => c.Name == "AppId");
        Assert.Contains(table.Columns, c => c.Name == "SubjectId");
    }

    [Fact]
    public void 建表DDL_SQLServer用方括号与uniqueidentifier()
    {
        var ddl = BuildCreateDdl(Sql, DatabaseSourceType.SqlServer);

        Assert.Single(ddl);
        Assert.Contains("CREATE TABLE [Saga_TestSaga]", ddl[0]);
        Assert.Contains("[OrderId] uniqueidentifier", ddl[0]);
        Assert.Contains("PRIMARY KEY", ddl[0]);
    }

    [Fact]
    public void 建表DDL_Postgres用小写表名与uuid()
    {
        var ddl = BuildCreateDdl(Pg, DatabaseSourceType.PostgreSQL);

        Assert.Single(ddl);
        // PG 的 Quote 是直通（不加引号），表名必须已经是 snake_case ——
        // 留成 Saga_TestSaga 会被 PG 折叠成 saga_testsaga，和 EF 的 ToTable 对不上
        Assert.Contains("CREATE TABLE saga_test_saga", ddl[0]);
        Assert.Contains("order_id uuid", ddl[0]);
        Assert.Contains("PRIMARY KEY", ddl[0]);
    }

    [Fact]
    public void 表名双方言不同但各自与EF的ToTable口径一致()
    {
        var sql = Assert.Single(VivSagaTableCreator.BuildExpectedTables([typeof(TestSaga)], DatabaseSourceType.SqlServer, Sql));
        var pg = Assert.Single(VivSagaTableCreator.BuildExpectedTables([typeof(TestSaga)], DatabaseSourceType.PostgreSQL, Pg));

        Assert.Equal("Saga_TestSaga", sql.Name);
        Assert.Equal("saga_test_saga", pg.Name);
    }

    private static List<string> BuildCreateDdl(SchemaSynchronizer synchronizer, DatabaseSourceType source)
    {
        var table = Assert.Single(VivSagaTableCreator.BuildExpectedTables([typeof(TestSaga)], source, synchronizer));

        var tableDiff = new TableDiff
        {
            TableName = table.Name,
            Type = DiffType.Added,
            ColumnDiffs = table.Columns
                .Select(c => new ColumnDiff
                {
                    TableName = table.Name,
                    ColumnName = c.Name,
                    Type = DiffType.Added,
                    Expected = c
                })
                .ToList()
        };

        return synchronizer.GenerateDdl(new SyncDiffResult { NewTables = [tableDiff] });
    }
}
