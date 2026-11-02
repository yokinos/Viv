using System;
using System.Collections.Generic;
using Viv.Engine;

namespace Viv.Engine.Tests;

/// <summary>
/// 网关短名：配置优先、推断兜底、格式校验、重名检查。
/// 环境变量那部分在 <see cref="AspireServiceDiscoveryTests"/>，这里只钉纯逻辑。
/// </summary>
public class GatewayShortNameTests
{
    private static readonly string[] Names =
    [
        "viv-apex-api", "viv-herta-api", "viv-herta-link", "viv-deepred-api"
    ];

    [Fact]
    public void 没配置时按服务名推断()
    {
        Assert.Equal("apex", AspireServiceDiscovery.ResolveShortName("viv-apex-api", Names));
        Assert.Equal("deepred", AspireServiceDiscovery.ResolveShortName("viv-deepred-api", Names));
    }

    [Fact]
    public void 第二段冲突时_api保留基础名_其它拼接剩余段()
    {
        Assert.Equal("herta", AspireServiceDiscovery.ResolveShortName("viv-herta-api", Names));
        Assert.Equal("hertalink", AspireServiceDiscovery.ResolveShortName("viv-herta-link", Names));
    }

    [Fact]
    public void 配了就用配置_并统一转小写()
    {
        var configured = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["viv-herta-link"] = "HT-Link"
        };

        Assert.Equal("ht-link", AspireServiceDiscovery.ResolveShortName("viv-herta-link", Names, configured));
    }

    [Theory]
    [InlineData("a b")]
    [InlineData("a/b")]
    [InlineData("中文")]
    [InlineData("")]
    public void 短名不合法直接抛(string shortName)
    {
        var configured = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["viv-apex-api"] = shortName
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => AspireServiceDiscovery.ResolveShortName("viv-apex-api", Names, configured));

        Assert.Contains("网关短名不合法", ex.Message);
    }

    [Fact]
    public void 两个服务撞同一个短名直接抛_并点名两个服务()
    {
        var services = new List<AspireServiceDiscovery.GatewayService>
        {
            new("viv-herta-api", new Uri("http://localhost:1"), "herta"),
            new("viv-herta-link", new Uri("http://localhost:2"), "herta")
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => AspireServiceDiscovery.EnsureUniqueShortNames(services));

        Assert.Contains("viv-herta-api", ex.Message);
        Assert.Contains("viv-herta-link", ex.Message);
    }

    [Fact]
    public void 短名不重复时通过()
    {
        var services = new List<AspireServiceDiscovery.GatewayService>
        {
            new("viv-herta-api", new Uri("http://localhost:1"), "herta"),
            new("viv-herta-link", new Uri("http://localhost:2"), "hertalink")
        };

        AspireServiceDiscovery.EnsureUniqueShortNames(services);
    }
}
