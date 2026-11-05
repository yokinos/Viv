using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Viv.Engine.Tests;

/// <summary>
/// 网关转发前的 query 清洗：身份参数（客户端可伪造）与 JWT 载体（网关用完就不该再往下传）都必须剥掉，
/// 其余业务参数原样保留。
/// </summary>
public class GatewayQueryStrippingTests
{
    private static QueryString? Strip(params (string Key, string Value)[] pairs)
    {
        var dict = pairs.ToDictionary(p => p.Key, p => new StringValues(p.Value));
        return VivStartGatewayExtensions.StripSensitiveQuery(new QueryCollection(dict));
    }

    [Fact]
    public void access_token被剥离_业务参数保留()
    {
        var result = Strip(("access_token", "eyJhbGciOi"), ("page", "2"));

        Assert.NotNull(result);
        Assert.Equal("?page=2", result!.Value.Value);
        Assert.DoesNotContain("eyJhbGciOi", result.Value.Value);
    }

    [Fact]
    public void refresh_token与id_token也被剥离()
    {
        var result = Strip(("refresh_token", "r1"), ("id_token", "i1"), ("id", "9"));

        Assert.NotNull(result);
        Assert.Equal("?id=9", result!.Value.Value);
    }

    [Theory]
    [InlineData("subjectId")]
    [InlineData("userId")]
    [InlineData("appId")]
    public void 可伪造的身份参数被剥离(string key)
    {
        var result = Strip((key, "123"), ("page", "1"));

        Assert.NotNull(result);
        Assert.Equal("?page=1", result!.Value.Value);
    }

    [Fact]
    public void 键名大小写不敏感()
    {
        var result = Strip(("Access_Token", "eyJhbGciOi"), ("Page", "1"));

        Assert.NotNull(result);
        Assert.Equal("?Page=1", result!.Value.Value);
    }

    [Fact]
    public void 没有敏感参数时返回null_调用方保持原QueryString()
    {
        // 返回 null 是「不用改写」的信号；若返回空 QueryString，中间件会把正常的 query 一起抹掉
        Assert.Null(Strip(("page", "1"), ("keyword", "abc")));
    }

    [Fact]
    public void 空query返回null()
    {
        Assert.Null(VivStartGatewayExtensions.StripSensitiveQuery(new QueryCollection()));
    }

    [Fact]
    public void 全部参数都要剥离时返回空QueryString()
    {
        var result = Strip(("access_token", "t"), ("userId", "1"));

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result!.Value.Value);
    }
}
