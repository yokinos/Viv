using Microsoft.EntityFrameworkCore;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Delusion.Magic;
using Viv.Momo.Core;
using Viv.Momo.Enums;
using Viv.Momo.Options;

namespace Viv.Momo.Tests;

/// <summary>单元测试用的租户访问器桩</summary>
public class StubTenantAccessor : IVivContextAccessor
{
    public VivContextContent? Current { get; set; }
}

/// <summary>
/// 暴露 EFAppContext.OnModelCreating 的测试子类（不接数据库，只验模型元数据）。
/// </summary>
public class ExposedEfAppContext : EFAppContext
{
    public ExposedEfAppContext(DatabaseOptions options, IVivContextAccessor? accessor)
        : base(options, accessor, DbReadWriteType.Write)
    {
    }

    public ModelBuilder BuildModel()
    {
        var modelBuilder = new ModelBuilder();
        OnModelCreating(modelBuilder);
        return modelBuilder;
    }
}

/// <summary>
/// 多租户隔离 —— 两条强制路径的纯逻辑验证：
/// 1) Dapper 路径：SqlMagic.AppendTenantFilter（经 GetDeleteSql / GetSoftDeleteSql / GetFindSqlTemplate 暴露）
/// 2) EF 路径：EFAppContext.OnModelCreating 对 ITenant 实体加全局查询过滤
/// </summary>
public class TenantFilterTests
{
    #region Dapper 路径

    [Fact]
    public void GetDeleteSql_租户实体带租户追加过滤()
    {
        var (sql, p) = SqlMagic.GetDeleteSql<TenantUserEntity>("users", x => x.Name == "a", DatabaseSourceType.SqlServer, tenantId: 7);

        Assert.Equal("DELETE FROM users WHERE ([Name] = @p0) AND [TenantId] = @TenantId", sql);
        Assert.Equal(7L, p["TenantId"]);
    }

    [Fact]
    public void GetDeleteSql_无请求上下文不追加()
    {
        var (sql, p) = SqlMagic.GetDeleteSql<TenantUserEntity>("users", x => x.Name == "a", DatabaseSourceType.SqlServer, tenantId: 0);

        Assert.Equal("DELETE FROM users WHERE ([Name] = @p0)", sql);
        Assert.DoesNotContain("TenantId", p.Keys);
    }

    [Fact]
    public void GetDeleteSql_非租户实体永不追加()
    {
        var (sql, _) = SqlMagic.GetDeleteSql<NonTenantEntity>("users", x => x.Name == "a", DatabaseSourceType.SqlServer, tenantId: 7);

        Assert.DoesNotContain("TenantId", sql);
    }

    [Fact]
    public void GetSoftDeleteSql_租户软删带过滤()
    {
        var (sql, p) = SqlMagic.GetSoftDeleteSql<SoftDeleteTenantEntity>("[users]", x => x.Name == "a", DatabaseSourceType.SqlServer, tenantId: 7);

        Assert.Equal("UPDATE [users] SET [IsDeleted] = 1, [DeletedAt] = GETDATE() WHERE ([Name] = @p0) AND [TenantId] = @TenantId", sql);
        Assert.Equal(7L, p["TenantId"]);
    }

    [Fact]
    public void GetSoftDeleteSql_Postgres语法()
    {
        var (sql, _) = SqlMagic.GetSoftDeleteSql<SoftDeleteTenantEntity>("users", x => x.Name == "a", DatabaseSourceType.PostgreSQL, tenantId: 7);

        Assert.Equal("UPDATE users SET isdeleted = true, deletedat = NOW() WHERE (name = @p0) AND tenantid = @TenantId", sql);
    }

    #endregion

    #region EF 查询过滤元数据

    private static DatabaseOptions EntityScanOptions()
        => new()
        {
            DatabaseSource = DatabaseSourceType.SqlServer,
            EntityTypeOptions =
            [
                new FilterTypeOptions { AssemblyName = "Viv.Momo.Tests", ClassNameEndsWith = "Entity" }
            ]
        };

    [Fact]
    public void EfOnModelCreating_ITenant实体加查询过滤()
    {
        var ctx = new ExposedEfAppContext(EntityScanOptions(), new StubTenantAccessor());
        var model = ctx.BuildModel();

        Assert.NotNull(model.Entity(typeof(TenantUserEntity)).Metadata.GetQueryFilter());
        Assert.NotNull(model.Entity(typeof(SoftDeleteTenantEntity)).Metadata.GetQueryFilter());
    }

    [Fact]
    public void EfOnModelCreating_非租户实体不加过滤()
    {
        var ctx = new ExposedEfAppContext(EntityScanOptions(), new StubTenantAccessor());
        var model = ctx.BuildModel();

        Assert.Null(model.Entity(typeof(NonTenantEntity)).Metadata.GetQueryFilter());
    }

    [Fact]
    public void EfOnModelCreating_无租户访问器不启用过滤()
    {
        var ctx = new ExposedEfAppContext(EntityScanOptions(), null);
        var model = ctx.BuildModel();

        Assert.Null(model.Entity(typeof(TenantUserEntity)).Metadata.GetQueryFilter());
    }

    #endregion
}
