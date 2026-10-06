using System;
using System.Collections.Generic;
using System.Linq;
using Viv.Contracts.Interface;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace Viv.Engine
{
    /// <summary>
    /// 从 Aspire 服务发现自动生成网关路由（零手写 JSON）：
    ///   /{short}/api/{**catch-all}  -> /api/{**catch-all}  （标准 API，匹配替换）
    ///   /docs/{short}/{**catch-all} -> /{**catch-all}     （Scalar 文档经网关透出）
    ///   /ws/{short}/{**catch-all}   -> /{**catch-all}     （SignalR / WebSocket 透传，如 hertalink 的 /chat hub）
    ///
    /// 路由不带 AuthorizationPolicy —— 网关不强制鉴权，只解析透传上下文头，由下游服务自行鉴权。
    /// 短名 = Aspire 服务名 split('-') 第二段（viv-apex-api -> apex），冲突时拼接（viv-herta-link -> hertalink）。
    /// </summary>
    public class VivGatewayRouteBuilder : IGatewayRouteProvider
    {
        public (IReadOnlyList<RouteConfig> Routes, IReadOnlyList<ClusterConfig> Clusters) Build()
        {
            var services = AspireServiceDiscovery.Load();
            var clusters = new List<ClusterConfig>(services.Count);
            var routes = new List<RouteConfig>(services.Count * 3);

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
                    // 流式接口（SSE）与长回答：YARP 默认活动超时 100 秒，空闲超了就直接掐断连接；
                    // 这里放宽到 30 分钟，并显式关闭响应缓冲，保证逐帧转发而不是攒完再发。
                    HttpRequest = new ForwarderRequestConfig
                    {
                        ActivityTimeout = TimeSpan.FromMinutes(30),
                        AllowResponseBuffering = false
                    }
                });

                // 标准 API：/apex/api/account/apexLogin -> /api/account/apexLogin
                // 挂 CustomRateLimiter 策略（读 viv.ratelimit.json），否则 AddRateLimiter 注册的策略形同虚设
                routes.Add(new RouteConfig
                {
                    RouteId = $"{shortName}-api",
                    ClusterId = clusterId,
                    Match = new RouteMatch { Path = $"/{shortName}/api/{{**catch-all}}" },
                    RateLimiterPolicy = VivStartGatewayExtensions.CustomRateLimiterPolicyName,
                    Transforms =
                    [
                        new Dictionary<string, string> { ["PathPattern"] = $"/api/{{**catch-all}}" }
                    ]
                });

                // Scalar 文档：/docs/apex/scalar/ -> /scalar/
                routes.Add(new RouteConfig
                {
                    RouteId = $"{shortName}-docs",
                    ClusterId = clusterId,
                    Match = new RouteMatch { Path = $"/docs/{shortName}/{{**catch-all}}" },
                    Transforms =
                    [
                        new Dictionary<string, string> { ["PathPattern"] = "/{**catch-all}" }
                    ]
                });

                // WebSocket / SignalR：/ws/hertalink/chat -> /chat
                routes.Add(new RouteConfig
                {
                    RouteId = $"{shortName}-ws",
                    ClusterId = clusterId,
                    Match = new RouteMatch { Path = $"/ws/{shortName}/{{**catch-all}}" },
                    Transforms =
                    [
                        new Dictionary<string, string> { ["PathPattern"] = "/{**catch-all}" }
                    ]
                });
            }

            return (routes, clusters);
        }
    }
}
