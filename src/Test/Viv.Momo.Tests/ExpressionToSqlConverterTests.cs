using Viv.Momo.Converter;
using Viv.Momo.Enums;

namespace Viv.Momo.Tests;

/// <summary>
/// 表达式 → 参数化 SQL —— 业务删改的 where 条件由表达式树编译而来（Dapper 谓词删除/更新）。
/// 这是除租户过滤外 Momo 的另一条核心路径。
/// </summary>
public class ExpressionToSqlConverterTests
{
    [Fact]
    public void 相等_Postgres字段小写()
    {
        var (sql, p) = ExpressionToSqlConverter.Convert<TenantUserEntity>(x => x.Id == 5, DatabaseSourceType.PostgreSQL);
        Assert.Equal("(id = @p0)", sql);
        Assert.Equal(5L, Convert.ToInt64(p["@p0"]));
    }

    [Fact]
    public void 相等_SqlServer加方括号()
    {
        var (sql, _) = ExpressionToSqlConverter.Convert<TenantUserEntity>(x => x.Id == 5, DatabaseSourceType.SqlServer);
        Assert.Equal("([Id] = @p0)", sql);
    }

    [Fact]
    public void And_拼接两个条件()
    {
        var (sql, _) = ExpressionToSqlConverter.Convert<TenantUserEntity>(
            x => x.TenantId == 7 && x.Age > 18, DatabaseSourceType.PostgreSQL);

        Assert.Equal("((tenantid = @p0) AND (age > @p1))", sql);
    }

    [Fact]
    public void Or_拼接两个条件()
    {
        var (sql, _) = ExpressionToSqlConverter.Convert<TenantUserEntity>(
            x => x.Name == "a" || x.Name == "b", DatabaseSourceType.PostgreSQL);

        Assert.Equal("((name = @p0) OR (name = @p1))", sql);
    }

    [Fact]
    public void 字符串Contains_转LIKE模糊()
    {
        var (sql, p) = ExpressionToSqlConverter.Convert<TenantUserEntity>(
            x => x.Name.Contains("abc"), DatabaseSourceType.PostgreSQL);

        Assert.Equal("name LIKE @p0", sql);
        Assert.Equal("%abc%", p["@p0"]);
    }

    [Fact]
    public void 字符串StartsWith_转LIKE前缀()
    {
        var (sql, p) = ExpressionToSqlConverter.Convert<TenantUserEntity>(
            x => x.Name.StartsWith("pre"), DatabaseSourceType.PostgreSQL);

        Assert.Equal("name LIKE @p0", sql);
        Assert.Equal("pre%", p["@p0"]);
    }

    [Fact]
    public void 闭包变量参数化()
    {
        int threshold = 18;
        var (sql, p) = ExpressionToSqlConverter.Convert<TenantUserEntity>(
            x => x.Age > threshold, DatabaseSourceType.PostgreSQL);

        Assert.Equal("(age > @p0)", sql);
        Assert.Equal(18, p["@p0"]);
    }

    [Fact]
    public void 不支持运算符_抛异常()
    {
        Assert.Throws<NotSupportedException>(() =>
            ExpressionToSqlConverter.Convert<TenantUserEntity>(x => x.Id + 1 > 3, DatabaseSourceType.PostgreSQL));
    }

    [Fact]
    public void 空表达式_返回空()
    {
        var (sql, p) = ExpressionToSqlConverter.Convert<TenantUserEntity>(null!, DatabaseSourceType.PostgreSQL);
        Assert.Equal(string.Empty, sql);
        Assert.Empty(p);
    }
}
