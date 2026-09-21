using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Viv.Engine.Options;
using Viv.Engine.Power;

namespace Viv.Engine.Tests;

public class VivForwardedHeadersTests
{
    [Fact]
    public void 未配置TrustedProxies_清空默认loopback限制()
    {
        var options = VivForwardedHeaders.Create(new EnvOptions());

        Assert.Empty(options.KnownIPNetworks);
        Assert.Empty(options.KnownProxies);
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor));
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedProto));
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedHost));
    }

    [Fact]
    public void 空数组同样清空()
    {
        var options = VivForwardedHeaders.Create(new EnvOptions { TrustedProxies = [] });
        Assert.Empty(options.KnownIPNetworks);
        Assert.Empty(options.KnownProxies);
    }

    [Fact]
    public void 配置IP与CIDR()
    {
        var options = VivForwardedHeaders.Create(new EnvOptions
        {
            TrustedProxies = ["10.0.0.1", "10.1.0.0/16", "  "]
        });

        Assert.Contains(options.KnownProxies, ip => ip.Equals(IPAddress.Parse("10.0.0.1")));
        Assert.Contains(options.KnownIPNetworks, n => n.PrefixLength == 16);
        Assert.DoesNotContain(options.KnownProxies, ip => IPAddress.IsLoopback(ip));
    }
}
