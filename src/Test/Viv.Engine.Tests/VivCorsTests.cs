using Microsoft.AspNetCore.Cors.Infrastructure;
using Viv.Engine;

namespace Viv.Engine.Tests;

public class VivCorsTests
{
    [Fact]
    public void 生产未配置Origins_不放行任何跨域来源()
    {
        // 这是 P0 回归点：入库 appsettings.json 的 Env=0 曾让网关把「dev 放行任意 Origin」带到生产。
        // 现在 isDevelopment 只由宿主环境决定，测试直接钉住 Apply 的行为。
        var builder = new CorsPolicyBuilder();
        VivCors.Apply(builder, origins: null, isDevelopment: false, allowCredentials: true);
        var policy = builder.Build();

        Assert.False(policy.AllowAnyOrigin);
        Assert.Empty(policy.Origins);
        Assert.False(policy.IsOriginAllowed("https://evil.example"));
        Assert.False(policy.IsOriginAllowed("http://localhost:5173"));
        Assert.True(policy.SupportsCredentials);
    }

    [Fact]
    public void 开发未配置Origins_回显任意来源()
    {
        var builder = new CorsPolicyBuilder();
        VivCors.Apply(builder, origins: null, isDevelopment: true, allowCredentials: true);
        var policy = builder.Build();

        Assert.False(policy.AllowAnyOrigin); // 用回显而非 *，才能和 AllowCredentials 共存
        Assert.True(policy.IsOriginAllowed("http://192.168.1.20:5173"));
    }

    [Fact]
    public void 配置了Origins_只放行列出的来源_开发环境也不放开()
    {
        var builder = new CorsPolicyBuilder();
        VivCors.Apply(builder, ["https://app.example.com"], isDevelopment: true, allowCredentials: true);
        var policy = builder.Build();

        Assert.True(policy.IsOriginAllowed("https://app.example.com"));
        Assert.False(policy.IsOriginAllowed("https://evil.example"));
    }

    [Theory]
    [InlineData("http://localhost:3000", true)]
    [InlineData("https://127.0.0.1:5173", true)]
    [InlineData("http://[::1]:8080", true)]
    [InlineData("https://evil.example", false)]
    [InlineData("not-a-uri", false)]
    public void 本机回环Origin判定(string origin, bool expected)
    {
        Assert.Equal(expected, VivCors.IsLoopbackOrigin(origin));
    }
}
