using Viv.Momo.Enums;

namespace Viv.Momo.Tests;

/// <summary>
/// SqlMagic 跨数据库 SQL 生成 —— 纯字符串逻辑，零数据库依赖。
/// 覆盖标识符引用、Insert/Select/Update/Delete 模板、分页、字面量转换。
/// </summary>
public class SqlMagicTests
{
    #region 标识符引用

    [Fact]
    public void QuoteIdentifier_SqlServer加方括号()
    {
        Assert.Equal("[Name]", SqlMagic.QuoteIdentifier("Name", DatabaseSourceType.SqlServer));
    }

    [Fact]
    public void QuoteIdentifier_Postgres转小写()
    {
        Assert.Equal("name", SqlMagic.QuoteIdentifier("Name", DatabaseSourceType.PostgreSQL));
    }

    [Fact]
    public void GetTableName_无Table特性用类名()
    {
        Assert.Equal("[TenantUserEntity]", SqlMagic.GetTableName<TenantUserEntity>(DatabaseSourceType.SqlServer));
        Assert.Equal("tenantuserentity", SqlMagic.GetTableName<TenantUserEntity>(DatabaseSourceType.PostgreSQL));
    }

    [Fact]
    public void GetTableName_读Table特性()
    {
        Assert.Equal("[sys_users]", SqlMagic.GetTableName<TableNamedEntity>(DatabaseSourceType.SqlServer));
        Assert.Equal("sys_users", SqlMagic.GetTableName<TableNamedEntity>(DatabaseSourceType.PostgreSQL));
    }

    #endregion

    #region Insert

    [Fact]
    public void GetInsertSqlTemplate_列名按库引用参数名保留原名()
    {
        var sqlServer = SqlMagic.GetInsertSqlTemplate("users", typeof(FlatRow), DatabaseSourceType.SqlServer);
        Assert.Equal("INSERT INTO users([Id],[Name],[Active]) VALUES(@Id,@Name,@Active)", sqlServer);

        var postgres = SqlMagic.GetInsertSqlTemplate("users", typeof(FlatRow), DatabaseSourceType.PostgreSQL);
        Assert.Equal("INSERT INTO users(id,name,active) VALUES(@Id,@Name,@Active)", postgres);
    }

    [Fact]
    public void CreateInsertSql_非空字段参数化()
    {
        var entity = new FlatRow { Id = 1, Name = "x", Active = true };
        var (sql, p) = SqlMagic.CreateInsertSql("users", entity, DatabaseSourceType.SqlServer);

        Assert.Equal("INSERT INTO users (Id,Name,Active) VALUES (@p0,@p1,@p2)", sql);
        Assert.Equal(1L, p.Get<object>("@p0"));
        Assert.Equal("x", p.Get<object>("@p1"));
        Assert.Equal(true, p.Get<object>("@p2"));
    }

    [Fact]
    public void CreateInsertSql_null字段跳过()
    {
        var entity = new NullableRow { Id = 1, Name = null };
        var (sql, p) = SqlMagic.CreateInsertSql("rows", entity, DatabaseSourceType.SqlServer);

        Assert.Equal("INSERT INTO rows (Id) VALUES (@p0)", sql);
        Assert.Single(p.ParameterNames);
    }

    [Fact]
    public void CreateInsertSql_ignoreKeys跳过指定列()
    {
        var entity = new FlatRow { Id = 1, Name = "x", Active = true };
        var (sql, _) = SqlMagic.CreateInsertSql("users", entity, DatabaseSourceType.PostgreSQL, ignoreKeys: "Id");

        Assert.Equal("INSERT INTO users (name,active) VALUES (@p0,@p1)", sql);
    }

    #endregion

    #region Update / Delete

    [Fact]
    public void CreateUpdateSql_whereKeys进WHERE其余进SET()
    {
        var entity = new FlatRow { Id = 1, Name = "x", Active = true };
        var (sql, p) = SqlMagic.CreateUpdateSql("users", entity, "Id", DatabaseSourceType.SqlServer);

        Assert.Equal("UPDATE users SET Name = @p1,Active = @p2 WHERE Id = @p0", sql);
        Assert.Equal("x", p.Get<object>("@p1"));
    }

    [Fact]
    public void CreateUpdateSql_无whereKeys抛异常()
    {
        var entity = new FlatRow { Id = 1 };
        Assert.Throws<ArgumentException>(() => SqlMagic.CreateUpdateSql("users", entity, "", DatabaseSourceType.SqlServer));
    }

    [Fact]
    public void CreateDeleteSql_全属性进WHERE()
    {
        var entity = new FlatRow { Id = 1, Name = "x", Active = true };
        var (sql, _) = SqlMagic.CreateDeleteSql("users", entity, DatabaseSourceType.SqlServer);

        Assert.Equal("DELETE FROM users WHERE Id = @p0 AND Name = @p1 AND Active = @p2", sql);
    }

    #endregion

    #region Raw 内联值

    [Fact]
    public void CreateInsertSqlRaw_SqlServer布尔转1()
    {
        var entity = new FlatRow { Id = 1, Name = "x", Active = true };
        var sql = SqlMagic.CreateInsertSqlRaw("users", entity, DatabaseSourceType.SqlServer);
        Assert.Equal("INSERT INTO users (Id,Name,Active) VALUES (1,'x',1)", sql);
    }

    [Fact]
    public void CreateInsertSqlRaw_Postgres布尔转true()
    {
        var entity = new FlatRow { Id = 1, Name = "x", Active = true };
        var sql = SqlMagic.CreateInsertSqlRaw("users", entity, DatabaseSourceType.PostgreSQL);
        Assert.Equal("INSERT INTO users (id,name,active) VALUES (1,'x',true)", sql);
    }

    [Fact]
    public void CreateUpdateSqlRaw_内联值()
    {
        var entity = new FlatRow { Id = 1, Name = "x", Active = true };
        var sql = SqlMagic.CreateUpdateSqlRaw("users", entity, "Id", DatabaseSourceType.SqlServer);
        Assert.Equal("UPDATE users SET Name = 'x',Active = 1 WHERE Id = 1", sql);
    }

    [Fact]
    public void CreateDeleteSqlRaw_内联值()
    {
        var entity = new FlatRow { Id = 1, Name = "x", Active = true };
        var sql = SqlMagic.CreateDeleteSqlRaw("users", entity, DatabaseSourceType.PostgreSQL);
        Assert.Equal("DELETE FROM users WHERE id = 1 AND name = 'x' AND active = true", sql);
    }

    #endregion

    #region Select 模板

    [Fact]
    public void GetFindSqlTemplate_按Id查()
    {
        Assert.Equal("SELECT * FROM users WHERE [Id] = @Id", SqlMagic.GetFindSqlTemplate("users", DatabaseSourceType.SqlServer));
        Assert.Equal("SELECT * FROM users WHERE id = @Id", SqlMagic.GetFindSqlTemplate("users", DatabaseSourceType.PostgreSQL));
    }

    [Fact]
    public void GetFindSqlTemplate_含租户过滤()
    {
        var sql = SqlMagic.GetFindSqlTemplate("users", DatabaseSourceType.SqlServer, includeTenantFilter: true);
        Assert.Equal("SELECT * FROM users WHERE [Id] = @Id AND [TenantId] = @TenantId", sql);
    }

    #endregion

    #region 分页

    [Fact]
    public void GetPageSqlTemplate_Postgres_LimitOffset()
    {
        var (page, count) = SqlMagic.GetPageSqlTemplate("SELECT * FROM users", 2, 10, DatabaseSourceType.PostgreSQL);

        Assert.Equal("SELECT * FROM users LIMIT 10 OFFSET 10", page);
        Assert.Equal("SELECT COUNT(*) FROM (SELECT * FROM users) AS t", count);
    }

    [Fact]
    public void GetPageSqlTemplate_CountSql剥离最外层OrderBy()
    {
        var (_, count) = SqlMagic.GetPageSqlTemplate("SELECT * FROM users ORDER BY Id DESC", 1, 10, DatabaseSourceType.PostgreSQL);

        Assert.Equal("SELECT COUNT(*) FROM (SELECT * FROM users) AS t", count);
    }

    [Fact]
    public void GetPageSqlTemplate_子查询OrderBy不剥离()
    {
        var sql = "SELECT * FROM (SELECT * FROM t ORDER BY x) s";
        var (_, count) = SqlMagic.GetPageSqlTemplate(sql, 1, 10, DatabaseSourceType.PostgreSQL);

        Assert.Equal("SELECT COUNT(*) FROM (SELECT * FROM (SELECT * FROM t ORDER BY x) s) AS t", count);
    }

    [Fact]
    public void GetPageSqlTemplate_SqlServer_OffsetFetch()
    {
        var (page, count) = SqlMagic.GetPageSqlTemplate("SELECT * FROM users ORDER BY Id DESC", 2, 10, DatabaseSourceType.SqlServer);

        Assert.Contains("OFFSET 10 ROWS FETCH NEXT 10 ROWS ONLY", page);
        Assert.Contains("ORDER BY", page);
        Assert.Contains("COUNT(1)", count);
    }

    [Fact]
    public void GetPageSqlTemplate_SqlServer无OrderBy抛异常()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SqlMagic.GetPageSqlTemplate("SELECT * FROM users", 1, 10, DatabaseSourceType.SqlServer));
    }

    #endregion

    #region 字面量转换

    [Fact]
    public void ToDatabaseValue_null转NULL()
    {
        Assert.Equal("NULL", SqlMagic.ToDatabaseValue(null, DatabaseSourceType.SqlServer));
    }

    [Fact]
    public void ToDatabaseValue_数字原样()
    {
        Assert.Equal("5", SqlMagic.ToDatabaseValue(5, DatabaseSourceType.SqlServer));
        Assert.Equal("123", SqlMagic.ToDatabaseValue(123L, DatabaseSourceType.SqlServer));
    }

    [Fact]
    public void ToDatabaseValue_字符串转义单引号()
    {
        Assert.Equal("'it''s'", SqlMagic.ToDatabaseValue("it's", DatabaseSourceType.SqlServer));
    }

    [Fact]
    public void ToDatabaseValue_布尔分库()
    {
        Assert.Equal("1", SqlMagic.ToDatabaseValue(true, DatabaseSourceType.SqlServer));
        Assert.Equal("true", SqlMagic.ToDatabaseValue(true, DatabaseSourceType.PostgreSQL));
    }

    [Fact]
    public void ToDatabaseValue_枚举转int()
    {
        Assert.Equal("2", SqlMagic.ToDatabaseValue(TestState.Active, DatabaseSourceType.SqlServer));
    }

    #endregion
}
