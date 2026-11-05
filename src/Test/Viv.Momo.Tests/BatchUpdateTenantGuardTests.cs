using Microsoft.EntityFrameworkCore;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Fakes;
using Viv.Momo.Core;
using Viv.Momo.DataFilter;
using Viv.Momo.Enums;
using Viv.Momo.Options;

namespace Viv.Momo.Tests;

/// <summary>
/// 批量 Update 的 Dapper 分支（≥200 条）不经过 EF 全局过滤器，租户条件必须自己拼。
/// 这些用例只断言生成的 SQL 与参数，不连库。
/// </summary>
public class BatchUpdateTenantGuardTests
{
    private static MomoDatabaseContext CreateDb(IVivContext context, DatabaseSourceType source)
    {
        var options = new DatabaseOptions
        {
            DatabaseSource = source,
            MasterConnectionString = source == DatabaseSourceType.PostgreSQL
                ? "Host=localhost;Database=viv_test"
                : "Server=localhost;Database=viv_test;User Id=sa;Password=x;TrustServerCertificate=true",
            Timeout = 30
        };

        return new MomoDatabaseContext(context, new RecordingLogger(), new StaticDatabaseOptionsProvider(options));
    }

    private static TestContext TenantContext(long tenantId)
    {
        var context = new TestContext();
        context.SetSnapshot(new VivContextContent { SubjectId = tenantId });
        return context;
    }

    private static List<TenantUserEntity> Rows(params long[] ids)
        => ids.Select(id => new TenantUserEntity { Id = id, TenantId = 7, Name = "n" }).ToList();

    [Fact]
    public void 租户实体_批量更新SQL带租户条件()
    {
        using var db = CreateDb(TenantContext(7), DatabaseSourceType.SqlServer);
        var list = db.BuildUpdateSqlList(Rows(1, 2), db.GetEFContext(DbReadWriteType.Write));

        var sql = Assert.Single(list).Key;

        Assert.Contains("WHERE [Id] IN (@Id_1, @Id_2)", sql);
        Assert.Contains("AND [TenantId] = @VivBatchTenantId", sql);

        var parameters = list[0].Value;
        var name = parameters.ParameterNames.Single(n => n.Contains("VivBatchTenantId"));
        Assert.Equal(7L, parameters.Get<long>(name));
    }

    [Fact]
    public void 租户实体_Postgres列名按物理名()
    {
        using var db = CreateDb(TenantContext(7), DatabaseSourceType.PostgreSQL);
        var list = db.BuildUpdateSqlList(Rows(1), db.GetEFContext(DbReadWriteType.Write));

        var sql = Assert.Single(list).Key;

        Assert.Contains("AND tenant_id = @VivBatchTenantId", sql);
    }

    [Fact]
    public void 非租户实体_不加租户条件()
    {
        using var db = CreateDb(TenantContext(7), DatabaseSourceType.SqlServer);
        var entities = new List<NonTenantEntity> { new() { Id = 1, Name = "n" } };

        var list = db.BuildUpdateSqlList(entities, db.GetEFContext(DbReadWriteType.Write));

        var sql = Assert.Single(list).Key;
        Assert.DoesNotContain("TenantId", sql);
        Assert.DoesNotContain("VivBatchTenantId", sql);
    }

    /// <summary>
    /// 无请求上下文（Worker / 消息消费）时与全局过滤器同一语义：不知道是谁就不猜，不追加条件。
    /// </summary>
    [Fact]
    public void 无租户上下文_不加租户条件()
    {
        using var db = CreateDb(new TestContext(), DatabaseSourceType.SqlServer);

        var list = db.BuildUpdateSqlList(Rows(1), db.GetEFContext(DbReadWriteType.Write));

        Assert.DoesNotContain("VivBatchTenantId", Assert.Single(list).Key);
    }

    /// <summary>
    /// 调用方显式关掉租户过滤器（跨租户运维）时，SQL 也不该再偷偷加条件。
    /// </summary>
    [Fact]
    public void 显式关闭租户过滤器_不加租户条件()
    {
        using var db = CreateDb(TenantContext(7), DatabaseSourceType.SqlServer);

        using (DataFilterSwitch.Disable(typeof(TenantDataFilter)))
        {
            var list = db.BuildUpdateSqlList(Rows(1), db.GetEFContext(DbReadWriteType.Write));
            Assert.DoesNotContain("VivBatchTenantId", Assert.Single(list).Key);
        }
    }

    /// <summary>TenantId 是被保护的列，不允许被入参覆盖（否则等于自己把行搬到别的租户）。</summary>
    [Fact]
    public void 批量更新SQL不写TenantId列()
    {
        using var db = CreateDb(TenantContext(7), DatabaseSourceType.SqlServer);
        var list = db.BuildUpdateSqlList(Rows(1), db.GetEFContext(DbReadWriteType.Write));

        var sql = Assert.Single(list).Key;
        Assert.DoesNotContain("[TenantId] = CASE", sql);
    }
}
