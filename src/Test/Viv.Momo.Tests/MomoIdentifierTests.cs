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
        => Assert.Equal(snake, MomoIdentifier.ToSnakeCase(clr));

    [Fact]
    public void PostgreSQL_QuoteClr走snake_case不加引号()
    {
        Assert.Equal("tenant_id", MomoIdentifier.QuoteClr("TenantId", DatabaseSourceType.PostgreSQL));
        Assert.Equal("at_user", MomoIdentifier.ToPhysical("AtUser", DatabaseSourceType.PostgreSQL));
    }

    [Fact]
    public void SqlServer_QuoteClr保持PascalCase加方括号()
    {
        Assert.Equal("[TenantId]", MomoIdentifier.QuoteClr("TenantId", DatabaseSourceType.SqlServer));
        Assert.Equal("AtUser", MomoIdentifier.ToPhysical("AtUser", DatabaseSourceType.SqlServer));
        Assert.Equal("[AtUser]", MomoIdentifier.Quote("AtUser", DatabaseSourceType.SqlServer));
    }

    [Fact]
    public void 显式物理名只加引号不再改写()
    {
        Assert.Equal("sys_users", MomoIdentifier.Quote("sys_users", DatabaseSourceType.PostgreSQL));
        Assert.Equal("[sys_users]", MomoIdentifier.Quote("sys_users", DatabaseSourceType.SqlServer));
    }
}
