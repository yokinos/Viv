using Viv.Momo.Enums;

namespace Viv.Momo.Tests;

/// <summary>
/// 物理名单一来源。钉住的是与 EFCore.NamingConventions SnakeCaseNameRewriter 对齐的规则，
/// 以及 SQL Server 保持 CLR PascalCase。
/// </summary>
public class MomoIdentifierTests
{
    [Theory]
    [InlineData("TenantId", "tenant_id")]
    [InlineData("AtUser", "at_user")]
    [InlineData("SyncConventionRow", "sync_convention_row")]
    [InlineData("DisplayName", "display_name")]
    [InlineData("Id", "id")]
    [InlineData("HTMLParser", "html_parser")]
    public void ToSnakeCase_对齐EFNamingConventions(string clr, string snake)
        => Assert.Equal(snake, SqlMagic.For(DatabaseSourceType.PostgreSQL).ToSnakeCase(clr));

    [Fact]
    public void PostgreSQL_QuoteClr走snake_case不加引号()
    {
        Assert.Equal("tenant_id", SqlMagic.For(DatabaseSourceType.PostgreSQL).QuoteClr("TenantId"));
        Assert.Equal("at_user", SqlMagic.For(DatabaseSourceType.PostgreSQL).ToPhysical("AtUser"));
    }

    [Fact]
    public void SqlServer_QuoteClr保持PascalCase加方括号()
    {
        Assert.Equal("[TenantId]", SqlMagic.For(DatabaseSourceType.SqlServer).QuoteClr("TenantId"));
        Assert.Equal("AtUser", SqlMagic.For(DatabaseSourceType.SqlServer).ToPhysical("AtUser"));
        Assert.Equal("[AtUser]", SqlMagic.For(DatabaseSourceType.SqlServer).Quote("AtUser"));
    }

    [Fact]
    public void 显式物理名只加引号不再改写()
    {
        Assert.Equal("sys_users", SqlMagic.For(DatabaseSourceType.PostgreSQL).Quote("sys_users"));
        Assert.Equal("[sys_users]", SqlMagic.For(DatabaseSourceType.SqlServer).Quote("sys_users"));
    }
}
