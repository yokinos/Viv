using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Viv.Engine.Options;
using Viv.Engine.Power;

namespace Viv.Engine.Tests;

public class VivForwardedHeadersTests
{
    [Fact]
    public void 开发环境未配置_清空默认loopback限制()
    {
        var options = VivForwardedHeaders.Create(new EnvOptions(), isDevelopment: true);

        Assert.Empty(options.KnownIPNetworks);
        Assert.Empty(options.KnownProxies);
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor));
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedProto));
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedHost));
    }

    [Fact]
    public void 非开发环境未配置_保留默认只信任loopback()
    {
        var options = VivForwardedHeaders.Create(new EnvOptions(), isDevelopment: false);

        // 关键：不能清空 allow list，否则任意来源都能伪造 X-Forwarded-*。
        // 默认值里 loopback 落在 KnownIPNetworks（127.0.0.1/8、::1/128）或 KnownProxies，两者合计非空即可。
        Assert.True(options.KnownIPNetworks.Count > 0 || options.KnownProxies.Count > 0);
    }

    [Fact]
    public void 非开发环境配置IP与CIDR_只信任白名单()
    {
        var options = VivForwardedHeaders.Create(new EnvOptions
        {
            TrustedProxies = ["10.0.0.1", "10.1.0.0/16", "  "]
        }, isDevelopment: false);

        Assert.Contains(options.KnownProxies, ip => ip.Equals(IPAddress.Parse("10.0.0.1")));
        Assert.Contains(options.KnownIPNetworks, n => n.PrefixLength == 16);
        // 显式白名单覆盖默认值，loopback 不再隐式受信
        Assert.DoesNotContain(options.KnownProxies, ip => IPAddress.IsLoopback(ip));
    }

    [Fact]
    public void 白名单全是空串_退回默认而不是全部清空()
    {
        var options = VivForwardedHeaders.Create(new EnvOptions { TrustedProxies = ["", "  "] }, isDevelopment: false);

        Assert.True(options.KnownIPNetworks.Count > 0 || options.KnownProxies.Count > 0);
    }
}
