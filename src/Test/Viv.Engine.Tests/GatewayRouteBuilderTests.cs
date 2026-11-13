using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Viv.Engine;

namespace Viv.Engine.Tests;

/// <summary>
/// 网关路由生成的回归测试：docs 只能透出 scalar/openapi，不能再有任意路径旁路；
/// 所有自动生成的路由都必须挂限流策略（曾经 /docs 与 /ws 都没有）。
/// </summary>
[Collection("VivEngineStaticState")]
public class GatewayRouteBuilderTests
{
    [Fact]
    public void 文档路由只放scalar和openapi_没有全通配旁路()
    {
        WithServiceEnv(() =>
        {
            var (routes, _) = new VivGatewayRouteBuilder().Build();

            Assert.Contains(routes, r => r.RouteId == "apex-docs-scalar");
            Assert.Contains(routes, r => r.RouteId == "apex-docs-openapi");

            // 关键：/docs/apex/{**catch-all} 会把 /docs/apex/api/xxx 直接送到下游 /api/xxx
            Assert.DoesNotContain(routes, r => r.Match.Path == "/docs/apex/{**catch-all}");

            var scalar = routes.Single(r => r.RouteId == "apex-docs-scalar");
            Assert.Equal("/scalar/{**catch-all}", scalar.Transforms![0]["PathPattern"]);
            var openapi = routes.Single(r => r.RouteId == "apex-docs-openapi");
            Assert.Equal("/openapi/{**catch-all}", openapi.Transforms![0]["PathPattern"]);
        });
    }

    [Fact]
    public void 路由不再自己挂限流策略()
    {
        WithServiceEnv(() =>
        {
            var (routes, clusters) = new VivGatewayRouteBuilder().Build();

            Assert.NotEmpty(routes);

            // 限流改由 DistributedRateLimitMiddleware 在管道里统一做。
            // 路由上再挂一遍就是两道闸口，而框架那一道是每副本各算一份的本地计数 —— 比不挂更糟。
            Assert.All(routes, r => Assert.True(string.IsNullOrEmpty(r.RateLimiterPolicy)));

            // 每条路由都要指到一个真实集群，避免改路由表时漏配
            var clusterIds = clusters.Select(c => c.ClusterId!).ToHashSet(StringComparer.Ordinal);
            Assert.All(routes, r => Assert.Contains(r.ClusterId!, clusterIds));
        });
    }

    [Fact]
    public void api与ws的路径重写保持契约()
    {
        WithServiceEnv(() =>
        {
            var (routes, _) = new VivGatewayRouteBuilder().Build();

            var api = routes.Single(r => r.RouteId == "apex-api");
            Assert.Equal("/apex/api/{**catch-all}", api.Match.Path);
            Assert.Equal("/api/{**catch-all}", api.Transforms![0]["PathPattern"]);

            var ws = routes.Single(r => r.RouteId == "apex-ws");
            Assert.Equal("/ws/apex/{**catch-all}", ws.Match.Path);
            Assert.Equal("/{**catch-all}", ws.Transforms![0]["PathPattern"]);
        });
    }

    [Fact]
    public void 流式超时只由配置决定_默认1800秒()
    {
        WithServiceEnv(() =>
        {
            var (_, clusters) = new VivGatewayRouteBuilder().Build();
            var cluster = Assert.Single(clusters);
            Assert.Equal(TimeSpan.FromSeconds(1800), cluster.HttpRequest!.ActivityTimeout);
        });
    }

    [Fact]
    public void 服务有多个副本时每个副本生成一个目标()
    {
        WithServiceEnv(() =>
            {
                var (_, clusters) = new VivGatewayRouteBuilder().Build();

                var cluster = Assert.Single(clusters);
                Assert.Equal(2, cluster.Destinations!.Count);
                Assert.Equal("http://127.0.0.1:5088/", cluster.Destinations["d1"].Address);
                Assert.Equal("http://127.0.0.1:5089/", cluster.Destinations["d2"].Address);
            },
            ("services__viv-apex-api__http__1", "http://127.0.0.1:5089"));
    }

    private static void WithServiceEnv(Action action, params (string Key, string Value)[] extra)
    {
        var snapshot = SnapshotServiceEnv();
        try
        {
            ClearServiceEnv();
            Environment.SetEnvironmentVariable("services__viv-apex-api__http__0", "http://127.0.0.1:5088");
            foreach (var (key, value) in extra)
            {
                Environment.SetEnvironmentVariable(key, value);
            }

            action();
        }
        finally
        {
            ClearServiceEnv();
            foreach (var kv in snapshot)
            {
                Environment.SetEnvironmentVariable(kv.Key, kv.Value);
            }
        }
    }

    private static Dictionary<string, string?> SnapshotServiceEnv()
    {
        var snapshot = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = entry.Key?.ToString();
            if (key != null && key.StartsWith("services__", StringComparison.OrdinalIgnoreCase))
            {
                snapshot[key] = entry.Value?.ToString();
            }
        }

        return snapshot;
    }

    private static void ClearServiceEnv()
    {
        foreach (var key in SnapshotServiceEnv().Keys)
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }
}
