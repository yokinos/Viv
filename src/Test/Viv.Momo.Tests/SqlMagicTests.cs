using Viv.Momo.DataFilter;
using Viv.Momo.Enums;
using Viv.Momo.Interface;

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
        Assert.Equal("[Name]", SqlMagic.For(DatabaseSourceType.SqlServer).QuoteIdentifier("Name"));
    }

    [Fact]
    public void QuoteIdentifier_Postgres转snake_case()
    {
        Assert.Equal("name", SqlMagic.For(DatabaseSourceType.PostgreSQL).QuoteIdentifier("Name"));
        Assert.Equal("tenant_id", SqlMagic.For(DatabaseSourceType.PostgreSQL).QuoteIdentifier("TenantId"));
    }

    [Fact]
    public void GetTableName_无Table特性用类名()
    {
        Assert.Equal("[TenantUserEntity]", SqlMagic.For(DatabaseSourceType.SqlServer).GetTableName<TenantUserEntity>());
        Assert.Equal("tenant_user_entity", SqlMagic.For(DatabaseSourceType.PostgreSQL).GetTableName<TenantUserEntity>());
    }

    [Fact]
    public void GetTableName_读Table特性()
    {
        Assert.Equal("[sys_users]", SqlMagic.For(DatabaseSourceType.SqlServer).GetTableName<TableNamedEntity>());
        Assert.Equal("sys_users", SqlMagic.For(DatabaseSourceType.PostgreSQL).GetTableName<TableNamedEntity>());
    }

    #endregion

    #region Insert

    [Fact]
    public void GetInsertSqlTemplate_列名按库引用参数名保留原名()
    {
        var sqlServer = SqlMagic.For(DatabaseSourceType.SqlServer).GetInsertSqlTemplate("users", typeof(FlatRow));
        Assert.Equal("INSERT INTO users([Id],[Name],[Active]) VALUES(@Id,@Name,@Active)", sqlServer);

        var postgres = SqlMagic.For(DatabaseSourceType.PostgreSQL).GetInsertSqlTemplate("users", typeof(FlatRow));
        Assert.Equal("INSERT INTO users(id,name,active) VALUES(@Id,@Name,@Active)", postgres);
    }

    [Fact]
    public void CreateInsertSql_非空字段参数化()
    {
        var entity = new FlatRow { Id = 1, Name = "x", Active = true };
        var (sql, p) = SqlMagic.For(DatabaseSourceType.SqlServer).CreateInsertSql("users", entity);

        Assert.Equal("INSERT INTO [users] ([Id],[Name],[Active]) VALUES (@p0,@p1,@p2)", sql);
        Assert.Equal(1L, p.Get<object>("@p0"));
        Assert.Equal("x", p.Get<object>("@p1"));
        Assert.Equal(true, p.Get<object>("@p2"));
    }

    [Fact]
    public void CreateInsertSql_null字段跳过()
    {
        var entity = new NullableRow { Id = 1, Name = null };
        var (sql, p) = SqlMagic.For(DatabaseSourceType.SqlServer).CreateInsertSql("rows", entity);

        Assert.Equal("INSERT INTO [rows] ([Id]) VALUES (@p0)", sql);
        Assert.Single(p.ParameterNames);
    }

    [Fact]
    public void CreateInsertSql_ignoreKeys跳过指定列()
    {
        var entity = new FlatRow { Id = 1, Name = "x", Active = true };
        var (sql, _) = SqlMagic.For(DatabaseSourceType.PostgreSQL).CreateInsertSql("users", entity, ignoreKeys: "Id");

        Assert.Equal("INSERT INTO users (name,active) VALUES (@p0,@p1)", sql);
    }

    #endregion

    #region Update / Delete

    [Fact]
    public void CreateUpdateSql_whereKeys进WHERE其余进SET()
    {
        var entity = new FlatRow { Id = 1, Name = "x", Active = true };
        var (sql, p) = SqlMagic.For(DatabaseSourceType.SqlServer).CreateUpdateSql("users", entity, "Id");

        Assert.Equal("UPDATE [users] SET [Name] = @p1,[Active] = @p2 WHERE [Id] = @p0", sql);
        Assert.Equal("x", p.Get<object>("@p1"));
    }

    [Fact]
    public void CreateUpdateSql_无whereKeys抛异常()
    {
        var entity = new FlatRow { Id = 1 };
        Assert.Throws<ArgumentException>(() => SqlMagic.For(DatabaseSourceType.SqlServer).CreateUpdateSql("users", entity, ""));
    }

    [Fact]
    public void CreateDeleteSql_全属性进WHERE()
    {
        var entity = new FlatRow { Id = 1, Name = "x", Active = true };
        var (sql, _) = SqlMagic.For(DatabaseSourceType.SqlServer).CreateDeleteSql("users", entity);

        Assert.Equal("DELETE FROM [users] WHERE [Id] = @p0 AND [Name] = @p1 AND [Active] = @p2", sql);
    }

    #endregion

    #region Raw 内联值

    [Fact]
    public void CreateInsertSqlRaw_SqlServer布尔转1()
    {
        var entity = new FlatRow { Id = 1, Name = "x", Active = true };
        var sql = SqlMagic.For(DatabaseSourceType.SqlServer).CreateInsertSqlRaw("users", entity);
        Assert.Equal("INSERT INTO [users] ([Id],[Name],[Active]) VALUES (1,'x',1)", sql);
    }

    [Fact]
    public void CreateInsertSqlRaw_Postgres布尔转true()
    {
        var entity = new FlatRow { Id = 1, Name = "x", Active = true };
        var sql = SqlMagic.For(DatabaseSourceType.PostgreSQL).CreateInsertSqlRaw("users", entity);
        Assert.Equal("INSERT INTO users (id,name,active) VALUES (1,'x',true)", sql);
    }

    [Fact]
    public void CreateUpdateSqlRaw_内联值()
    {
        var entity = new FlatRow { Id = 1, Name = "x", Active = true };
        var sql = SqlMagic.For(DatabaseSourceType.SqlServer).CreateUpdateSqlRaw("users", entity, "Id");
        Assert.Equal("UPDATE [users] SET [Name] = 'x',[Active] = 1 WHERE [Id] = 1", sql);
    }

    [Fact]
    public void CreateDeleteSqlRaw_内联值()
    {
        var entity = new FlatRow { Id = 1, Name = "x", Active = true };
        var sql = SqlMagic.For(DatabaseSourceType.PostgreSQL).CreateDeleteSqlRaw("users", entity);
        Assert.Equal("DELETE FROM users WHERE id = 1 AND name = 'x' AND active = true", sql);
    }

    #endregion

    #region Select 模板

    [Fact]
    public void GetFindSqlTemplate_按Id查()
    {
        Assert.Equal("SELECT * FROM users WHERE [Id] = @Id", SqlMagic.For(DatabaseSourceType.SqlServer).GetFindSqlTemplate("users"));
        Assert.Equal("SELECT * FROM users WHERE id = @Id", SqlMagic.For(DatabaseSourceType.PostgreSQL).GetFindSqlTemplate("users"));
    }

    /// <summary>从静态清单里挑出指定的几条过滤器 —— 与框架拼 SQL 时的取法一致</summary>
    private static IMomoDataFilter[] Filters(params Type[] filterTypes)
        => MomoDataFilters.All.Where(x => filterTypes.Contains(x.GetType())).ToArray();

    [Fact]
    public void GetFindSqlTemplate_含租户过滤()
    {
        var parameters = new Dictionary<string, object> { ["Id"] = 1L };
        var sql = SqlMagic.For(DatabaseSourceType.SqlServer).GetFindSqlTemplate("users", 7L, Filters(typeof(TenantDataFilter)), parameters);

        Assert.Equal("SELECT * FROM users WHERE [Id] = @Id AND [TenantId] = @TenantId", sql);
        Assert.Equal(7L, parameters["TenantId"]);
    }

    [Fact]
    public void GetFindSqlTemplate_含软删除过滤()
    {
        // 布尔字面量按方言给：PostgreSQL 的 boolean 列不接受 = 0
        Assert.Equal("SELECT * FROM users WHERE [Id] = @Id AND [IsDeleted] = 0",
            SqlMagic.For(DatabaseSourceType.SqlServer).GetFindSqlTemplate("users", 7L, Filters(typeof(SoftDeletedFilter)), new Dictionary<string, object>()));
        Assert.Equal("SELECT * FROM users WHERE id = @Id AND is_deleted = false",
            SqlMagic.For(DatabaseSourceType.PostgreSQL).GetFindSqlTemplate("users", 7L, Filters(typeof(SoftDeletedFilter)), new Dictionary<string, object>()));
    }

    [Fact]
    public void GetFindSqlTemplate_租户与软删除两条叠加()
    {
        var parameters = new Dictionary<string, object>();
        var sql = SqlMagic.For(DatabaseSourceType.SqlServer).GetFindSqlTemplate("users", 7L, Filters(typeof(TenantDataFilter), typeof(SoftDeletedFilter)), parameters);

        Assert.Equal("SELECT * FROM users WHERE [Id] = @Id AND [TenantId] = @TenantId AND [IsDeleted] = 0", sql);
    }

    [Fact]
    public void GetFindSqlTemplate_无租户时不加租户条件()
    {
        // 与 EF 那条「无上下文不过滤」同一语义：后台任务没有租户，不能凭空拼一个 TenantId = 0 进去
        var parameters = new Dictionary<string, object>();
        var sql = SqlMagic.For(DatabaseSourceType.SqlServer).GetFindSqlTemplate("users", 0L, Filters(typeof(TenantDataFilter)), parameters);

        Assert.Equal("SELECT * FROM users WHERE [Id] = @Id", sql);
        Assert.False(parameters.ContainsKey("TenantId"));
    }

    #endregion

    #region 分页

    [Fact]
    public void GetPageSqlTemplate_Postgres_LimitOffset()
    {
        var (page, count) = SqlMagic.For(DatabaseSourceType.PostgreSQL).GetPageSqlTemplate("SELECT * FROM users", 2, 10);

        Assert.Equal("SELECT * FROM users LIMIT 10 OFFSET 10", page);
        Assert.Equal("SELECT COUNT(*) FROM (SELECT * FROM users) AS t", count);
    }

    [Fact]
    public void GetPageSqlTemplate_CountSql剥离最外层OrderBy()
    {
        var (_, count) = SqlMagic.For(DatabaseSourceType.PostgreSQL).GetPageSqlTemplate("SELECT * FROM users ORDER BY Id DESC", 1, 10);

        Assert.Equal("SELECT COUNT(*) FROM (SELECT * FROM users) AS t", count);
    }

    [Fact]
    public void GetPageSqlTemplate_子查询OrderBy不剥离()
    {
        var sql = "SELECT * FROM (SELECT * FROM t ORDER BY x) s";
        var (_, count) = SqlMagic.For(DatabaseSourceType.PostgreSQL).GetPageSqlTemplate(sql, 1, 10);

        Assert.Equal("SELECT COUNT(*) FROM (SELECT * FROM (SELECT * FROM t ORDER BY x) s) AS t", count);
    }

    [Fact]
    public void GetPageSqlTemplate_SqlServer_OffsetFetch()
    {
        var (page, count) = SqlMagic.For(DatabaseSourceType.SqlServer).GetPageSqlTemplate("SELECT * FROM users ORDER BY Id DESC", 2, 10);

        Assert.Contains("OFFSET 10 ROWS FETCH NEXT 10 ROWS ONLY", page);
        Assert.Contains("ORDER BY", page);
        Assert.Contains("COUNT(1)", count);
    }

    [Fact]
    public void GetPageSqlTemplate_SqlServer无OrderBy抛异常()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SqlMagic.For(DatabaseSourceType.SqlServer).GetPageSqlTemplate("SELECT * FROM users", 1, 10));
    }

    #endregion

    #region 字面量转换

    [Fact]
    public void ToDatabaseValue_null转NULL()
    {
        Assert.Equal("NULL", SqlMagic.For(DatabaseSourceType.SqlServer).ToDatabaseValue(null));
    }

    [Fact]
    public void ToDatabaseValue_数字原样()
    {
        Assert.Equal("5", SqlMagic.For(DatabaseSourceType.SqlServer).ToDatabaseValue(5));
        Assert.Equal("123", SqlMagic.For(DatabaseSourceType.SqlServer).ToDatabaseValue(123L));
    }

    [Fact]
    public void ToDatabaseValue_字符串转义单引号()
    {
        Assert.Equal("'it''s'", SqlMagic.For(DatabaseSourceType.SqlServer).ToDatabaseValue("it's"));
    }

    [Fact]
    public void ToDatabaseValue_布尔分库()
    {
        Assert.Equal("1", SqlMagic.For(DatabaseSourceType.SqlServer).ToDatabaseValue(true));
        Assert.Equal("true", SqlMagic.For(DatabaseSourceType.PostgreSQL).ToDatabaseValue(true));
    }

    [Fact]
    public void ToDatabaseValue_枚举转int()
    {
        Assert.Equal("2", SqlMagic.For(DatabaseSourceType.SqlServer).ToDatabaseValue(TestState.Active));
    }

    #endregion
}
