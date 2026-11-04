using System;
using System.Collections.Generic;
using Viv.Contracts.Interface;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace Viv.Engine
{
    /// <summary>
    /// 从 Aspire 服务发现自动生成网关路由（零手写 JSON）：
    ///   /{short}/api/{**catch-all}         -> /api/{**catch-all}          （标准 API，匹配替换）
    ///   /docs/{short}/scalar/{**catch-all} -> /scalar/{**catch-all}       （Scalar 文档经网关透出）
    ///   /docs/{short}/openapi/{**catch-all}-> /openapi/{**catch-all}      （OpenAPI 文档）
    ///   /ws/{short}/{**catch-all}          -> /{**catch-all}              （SignalR / WebSocket 透传）
    ///
    /// 路由不带 AuthorizationPolicy —— 网关不强制鉴权，只解析透传上下文头，由下游服务自行鉴权。
    /// 短名优先读网关配置 VivGateway:ShortNames，没配才按服务名推断（见 <see cref="AspireServiceDiscovery.ResolveShortName"/>）。
    /// </summary>
    public class VivGatewayRouteBuilder : IGatewayRouteProvider
    {
        /// <summary>默认流式超时（SSE / 长回答）。YARP 默认 100 秒会掐断长连接。</summary>
        public static readonly TimeSpan DefaultStreamActivityTimeout = TimeSpan.FromMinutes(30);

        private readonly IReadOnlyDictionary<string, string>? _shortNames;
        private readonly TimeSpan _streamActivityTimeout;

        public VivGatewayRouteBuilder(IReadOnlyDictionary<string, string>? shortNames = null, TimeSpan? streamActivityTimeout = null)
        {
            _shortNames = shortNames;
            _streamActivityTimeout = streamActivityTimeout is { } timeout && timeout > TimeSpan.Zero
                ? timeout
                : DefaultStreamActivityTimeout;
        }

        public (IReadOnlyList<RouteConfig> Routes, IReadOnlyList<ClusterConfig> Clusters) Build()
        {
            var services = AspireServiceDiscovery.Load(_shortNames);
            var clusters = new List<ClusterConfig>(services.Count);
            var routes = new List<RouteConfig>(services.Count * 4);

            foreach (var service in services)
            {
                var shortName = service.ShortName;
                var clusterId = $"cluster-{shortName}";

                clusters.Add(new ClusterConfig
                {
                    ClusterId = clusterId,
                    Destinations = new Dictionary<string, DestinationConfig>
                    {
                        ["d1"] = new DestinationConfig { Address = service.Uri.ToString() }
                    },
                    // 流式接口（SSE）与长回答：默认活动超时 100 秒，空闲超了就直接掐断连接；
                    // 这里统一放宽（可由 VivGateway:StreamActivityTimeoutSeconds 调整），并显式关闭响应缓冲，
                    // 保证逐帧转发而不是攒完再发。
                    HttpRequest = new ForwarderRequestConfig
                    {
                        ActivityTimeout = _streamActivityTimeout,
                        AllowResponseBuffering = false
                    }
                });

                // 标准 API：/apex/api/account/apexLogin -> /api/account/apexLogin
                // 挂限流策略；不挂的话 AddRateLimiter 注册的策略不生效
                routes.Add(new RouteConfig
                {
                    RouteId = $"{shortName}-api",
                    ClusterId = clusterId,
                    Match = new RouteMatch { Path = $"/{shortName}/api/{{**catch-all}}" },
                    RateLimiterPolicy = VivStartGatewayExtensions.CustomRateLimiterPolicyName,
                    Transforms =
                    [
                        new Dictionary<string, string> { ["PathPattern"] = "/api/{**catch-all}" }
                    ]
                });

                // 文档：只透出 /scalar/** 与 /openapi/**。
                // 以前是 /docs/{short}/{**catch-all} 全通配，等于给下游任意路径（含 /api/**）开了一条
                // 不挂限流的旁路：/docs/apex/api/xxx 可直达 /api/xxx。
                AddDocsRoute(routes, clusterId, shortName, "scalar");
                AddDocsRoute(routes, clusterId, shortName, "openapi");

                // WebSocket / SignalR：/ws/hertalink/chat -> /chat
                // 路径保留通配：Hub 的挂载路径由各服务自己决定（MapKinnaHub<THub>("/chat")），
                // 网关不掌握这个清单。所以 /ws/{short}/api/xxx 在 HTTP 语义上也能打到下游 /api/xxx。
                // 身份边界靠两层：网关 JWT（access_token 只允许出现在 /ws 前缀下）+ 下游 [Authorize]；
                // 限流策略与 api 路由一致，不能再靠「docs 通配」那种无策略路径绕过。
                routes.Add(new RouteConfig
                {
                    RouteId = $"{shortName}-ws",
                    ClusterId = clusterId,
                    Match = new RouteMatch { Path = $"/ws/{shortName}/{{**catch-all}}" },
                    RateLimiterPolicy = VivStartGatewayExtensions.CustomRateLimiterPolicyName,
                    Transforms =
                    [
                        new Dictionary<string, string> { ["PathPattern"] = "/{**catch-all}" }
                    ]
                });
            }

            return (routes, clusters);
        }

        private static void AddDocsRoute(List<RouteConfig> routes, string clusterId, string shortName, string segment)
        {
            routes.Add(new RouteConfig
            {
                RouteId = $"{shortName}-docs-{segment}",
                ClusterId = clusterId,
                Match = new RouteMatch { Path = $"/docs/{shortName}/{segment}/{{**catch-all}}" },
                RateLimiterPolicy = VivStartGatewayExtensions.CustomRateLimiterPolicyName,
                Transforms =
                [
                    new Dictionary<string, string> { ["PathPattern"] = $"/{segment}/{{**catch-all}}" }
                ]
            });
        }
    }
}
