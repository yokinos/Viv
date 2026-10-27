using Viv.Momo.Dialect;
using Viv.Momo.Enums;

namespace Viv.Momo.Tests;

/// <summary>
/// 两个方言的"形状"断言：命名、引用符、字面量、分页。
/// 这些是两种库真正不同的地方 —— 之前它们藏在 <c>SqlMagic</c> 的 `if (databaseSource == ...)` 分支里。
/// </summary>
public class SqlDialectTests
{
    private static readonly ISqlDialect Sql = SqlMagic.For(DatabaseSourceType.SqlServer);

    private static readonly ISqlDialect Pg = SqlMagic.For(DatabaseSourceType.PostgreSQL);

    [Theory]
    [InlineData(DatabaseSourceType.SqlServer)]
    [InlineData(DatabaseSourceType.PostgreSQL)]
    public void 实例带正确的库类型(DatabaseSourceType source)
    {
        Assert.Equal(source, SqlMagic.For(source).Source);
    }

    [Fact]
    public void 未知库类型直接抛()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SqlMagic.For((DatabaseSourceType)999));
    }

    [Fact]
    public void SQLServer_表名保持PascalCase并加方括号()
    {
        Assert.Equal("[TenantUserEntity]", Sql.GetTableName<TenantUserEntity>());
        Assert.Equal("[DisplayName]", Sql.QuoteIdentifier("DisplayName"));
    }

    [Fact]
    public void Postgres_表名转snake_case且不加引用符()
    {
        Assert.Equal("tenant_user_entity", Pg.GetTableName<TenantUserEntity>());
        Assert.Equal("display_name", Pg.QuoteIdentifier("DisplayName"));
    }

    [Fact]
    public void 布尔字面量按方言()
    {
        Assert.Equal("1", Sql.ToDatabaseValue(true));
        Assert.Equal("0", Sql.ToDatabaseValue(false));

        Assert.Equal("true", Pg.ToDatabaseValue(true));
        Assert.Equal("false", Pg.ToDatabaseValue(false));
    }

    [Fact]
    public void 空值与字符串转义两边一致()
    {
        Assert.Equal("NULL", Sql.ToDatabaseValue(null));
        Assert.Equal("NULL", Pg.ToDatabaseValue(null));

        Assert.Equal("'a''b'", Sql.ToDatabaseValue("a'b"));
        Assert.Equal("'a''b'", Pg.ToDatabaseValue("a'b"));
    }

    [Fact]
    public void Postgres分页用LIMIT与OFFSET()
    {
        var (pageSql, countSql) = Pg.GetPageSqlTemplate("SELECT * FROM t ORDER BY id", pageIndex: 2, pageSize: 10);

        Assert.Equal("SELECT * FROM t ORDER BY id LIMIT 10 OFFSET 10", pageSql);
        Assert.Equal("SELECT COUNT(*) FROM (SELECT * FROM t) AS t", countSql);
    }

    [Fact]
    public void SQLServer分页用OFFSET与FETCH()
    {
        var (pageSql, countSql) = Sql.GetPageSqlTemplate("SELECT * FROM [T] ORDER BY [Id]", pageIndex: 2, pageSize: 10);

        Assert.Contains("OFFSET 10 ROWS", pageSql);
        Assert.Contains("FETCH NEXT 10 ROWS ONLY", pageSql);
        Assert.NotEmpty(countSql);
    }

    [Fact]
    public void Postgres软删除用NOW与true()
    {
        var (sql, _) = Pg.GetSoftDeleteSql<SoftDeleteTenantEntity>("users", x => x.Id == 1);

        Assert.Contains("is_deleted = true", sql);
        Assert.Contains("deleted_at = NOW()", sql);
    }

    [Fact]
    public void SQLServer软删除用GETDATE与1()
    {
        var (sql, _) = Sql.GetSoftDeleteSql<SoftDeleteTenantEntity>("users", x => x.Id == 1);

        Assert.Contains("[IsDeleted] = 1", sql);
        Assert.Contains("[DeletedAt] = GETDATE()", sql);
    }
}
