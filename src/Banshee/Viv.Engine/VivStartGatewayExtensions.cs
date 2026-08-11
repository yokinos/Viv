using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Serilog;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Viv.Aoi;
using Viv.Contracts.Models;
using Viv.Engine.Options;
using Yarp.ReverseProxy;
using Yarp.ReverseProxy.Configuration;

namespace Viv.Engine
{
    /// <summary>
    /// 启动网关（YARP 反向代理）。
    /// </summary>
    public static class VivStartGatewayExtensions
    {
        private const string DefaultCorsPolicyName = "DefaultCors";
        private const string NoCachePolicyName = "NoCache";
        private const string DefaultCachePolicyName = "DefaultCache";
        private const string CustomCachePolicyName = "CustomCache";
        internal const string DefaultRateLimiterPolicyName = "DefaultRateLimiter";
        internal const string CustomRateLimiterPolicyName = "CustomRateLimiter";
        private const int DefaultCacheSeconds = 20;
        private static readonly string[] _vivClaimTypes = ["tenantId", "userId", "appId"];

        /// <summary>
        /// 配置 Viv 网关基础服务：加载配置、限流配置热重载、Autofac、AddViv、JWT 解析、YARP（路由/集群从 Aspire 服务发现自动生成）、CORS、OutputCache、RateLimiter、编码注册。
        /// 需要先调用 builder.AddServiceDefaults()。
        /// </summary>
        /// <param name="serviceCollectionConfigure">追加网关服务</param>
        /// <param name="configureJwt">微调 JwtBearerOptions（events、挑战头等）</param>
        /// <param name="ignoreSslErrors">信任所有证书（仅开发环境应开启；默认 false，生产禁用避免 MITM）</param>
        /// <param name="rateLimitConfigFile">自定义限流策略配置，热重载</param>
        public static WebApplicationBuilder AddVivGateway(
            this WebApplicationBuilder builder,
            Action<IServiceCollection>? serviceCollectionConfigure = null,
            Action<JwtBearerOptions>? configureJwt = null,
            bool ignoreSslErrors = false,
            string rateLimitConfigFile = "viv.ratelimit.json")
        {
            var vivOptions = VivEngine.LoadVivConfig();
            ArgumentNullException.ThrowIfNull(vivOptions);

            // 限流配置热重载（路由/集群改为从 Aspire 服务发现自动生成，不再读 viv.yarp.json）
            builder.Configuration.AddJsonFile(rateLimitConfigFile, optional: false, reloadOnChange: true);

            // Autofac 容器
            builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
            builder.Host.ConfigureContainer<ContainerBuilder>(container =>
            {
                container.VivAutofacRegister(vivOptions.DIOption);
            });

            // 完整 Viv 链路（DIOption 等为 null 时各模块 null 守卫安全跳过）
            builder.Services.AddViv(vivOptions);

            if (vivOptions.LogOption != null && vivOptions.LogOption.LogType == Log.LogType.Serilog)
            {
                builder.Host.UseSerilog();
            }

            builder.Services.AddHttpContextAccessor();
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // 开发环境忽略 SSL 校验
            if (ignoreSslErrors)
            {
                builder.Services.AddSingleton<IHttpMessageHandlerBuilderFilter, IgnoreSslErrorsFilter>();
            }

            // JWT 对称密钥解析（读 viv.config.json 的 TokenOption）——只解析，不强制，用于认证后透传 x-viv-* 上下文头
            // SignalR/WebSocket 升级请求无法带 Authorization 头，补充 access_token 查询参数认证（SignalR 标准约定）。
            var jwtConfigure = configureJwt == null ? (Action<JwtBearerOptions>)AddAccessTokenFromQuery : options => { AddAccessTokenFromQuery(options); configureJwt(options); };
            VivJwtBearerHelper.ConfigureJwtBearer(builder.Services, vivOptions.TokenOption, jwtConfigure, throwIfMissing: true);

            // YARP 反向代理：路由/集群从 Aspire 服务发现（services__* 环境变量）自动生成，零手写 JSON
            var (gatewayRoutes, gatewayClusters) = VivGatewayRouteBuilder.Build();
            builder.Services.AddReverseProxy().LoadFromMemory(gatewayRoutes, gatewayClusters);

            // CORS
            builder.Services.AddCors(options =>
            {
                options.AddPolicy(DefaultCorsPolicyName, policy =>
                {
                    policy.SetIsOriginAllowedToAllowWildcardSubdomains().AllowAnyHeader().AllowAnyMethod().AllowCredentials();
                });
            });

            // OutputCache
            builder.Services.AddOutputCache(options =>
            {
                options.AddPolicy(NoCachePolicyName, build => build.NoCache());
                options.AddPolicy(DefaultCachePolicyName, build => build.Expire(TimeSpan.FromSeconds(DefaultCacheSeconds)));
                options.AddPolicy(CustomCachePolicyName, build => build.Expire(TimeSpan.FromSeconds(DefaultCacheSeconds)));
            });

            // RateLimiter
            builder.Services.Configure<VivRateLimitOptions>(builder.Configuration.GetSection(VivRateLimitOptions.CustomRateLimit));
            var rateLimitOptions = new VivRateLimitOptions();
            builder.Configuration.GetSection(VivRateLimitOptions.CustomRateLimit).Bind(rateLimitOptions);
            builder.Services.AddRateLimiter(options =>
            {
                options.AddFixedWindowLimiter(DefaultRateLimiterPolicyName, opt =>
                {
                    opt.PermitLimit = 4;
                    opt.Window = TimeSpan.FromSeconds(12);
                    opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    opt.QueueLimit = 2;
                });
                options.AddFixedWindowLimiter(CustomRateLimiterPolicyName, opt =>
                {
                    opt.PermitLimit = rateLimitOptions.PermitLimit;
                    opt.Window = TimeSpan.FromSeconds(rateLimitOptions.Window);
                    opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    opt.QueueLimit = rateLimitOptions.QueueLimit;
                });
            });

            serviceCollectionConfigure?.Invoke(builder.Services);
            return builder;
        }

        /// <summary>
        /// Build → VivLocator → WebSocket → CORS → OutputCache → RateLimiter → 认证（只解析不强制）→ 上下文头注入 → YARP → configure → Run。
        /// 网关不强制鉴权（路由无 AuthorizationPolicy）：token 有就解析透传 x-viv-* 上下文头，没有就不管，由下游服务自行鉴权。
        /// </summary>
        /// <param name="configure">末端回调，调用方在此调用 app.MapDefaultEndpoints()</param>
        /// <param name="configureReverseProxy">覆盖 YARP 默认管道（SessionAffinity/LoadBalancing/PassiveHealthChecks）</param>
        /// <param name="webSocketKeepAliveInterval">WebSocket KeepAlive 间隔，默认 15 秒</param>
        public static void RunVivGateway(
            this WebApplicationBuilder builder,
            Action<WebApplication>? configure = null,
            Action<IReverseProxyApplicationBuilder>? configureReverseProxy = null,
            TimeSpan? webSocketKeepAliveInterval = null)
        {
            var app = builder.Build();
            VivLocator.Initialize(app.Services);

            // 网关欢迎页（gateway.html）与 404 页面
            app.UseMiddleware<Middleware.ApiStartedMiddleware>();

            app.UseWebSockets(new WebSocketOptions
            {
                KeepAliveInterval = webSocketKeepAliveInterval ?? TimeSpan.FromSeconds(15)
            });

            app.UseCors(DefaultCorsPolicyName);
            app.UseOutputCache();
            app.UseRateLimiter();

            app.UseAuthentication();
            app.UseAuthorization();

            // 认证后把用户信息透传给下游（claims 仅在认证后可用）。
            // Header 契约与 RequestTokenAnalysisMagic 对齐：
            //   x-viv-appId / x-viv-subjectId(=TenantId) / x-viv-userId / x-viv-serviceName
            // 先剥离客户端可能伪造的 x-viv-* 上下文头与 x-request-token，只回填来自验签 token 的值。
            // 回填后对头组做 HMAC-SHA256 签名写入 x-request-token，下游验签通过才信任——防止绕过网关直连下游伪造头。
            app.Use(async (context, next) =>
            {
                foreach (var header in new[]
                {
                    VivRunDefine.AppIdHeader,
                    VivRunDefine.SubjectIdHeader,
                    VivRunDefine.UserIdHeader,
                    VivRunDefine.ServiceNameHeader,
                    VivRunDefine.InnerRequestTokenHeader
                })
                {
                    context.Request.Headers.Remove(header);
                }

                // 剥离客户端可伪造的身份 query 参数（tenantId/userId/appId）：
                // 身份只允许来自认证后回填的 x-viv-* 头，客户端经 query 直传的身份一律丢弃，防止冒充任意用户/租户。
                var spoofableIdentityKeys = _vivClaimTypes;
                if (context.Request.Query.Count > 0)
                {
                    var strippedQuery = context.Request.Query
                        .Where(kv => !spoofableIdentityKeys.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
                        .ToArray();
                    if (strippedQuery.Length != context.Request.Query.Count)
                    {
                        context.Request.QueryString = QueryString.Create(strippedQuery);
                    }
                }

                if (context.User.Identity?.IsAuthenticated == true)
                {
                    // .NET 10 JwtBearer 只映射 sub → NameIdentifier（name 保持短格式），两种都兼容。
                    var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? "";
                    context.Request.Headers[VivRunDefine.UserIdHeader] = userId;
                    context.Request.Headers[VivRunDefine.AppIdHeader] = context.User.FindFirstValue(VivClaimTypes.AppId) ?? "";
                    context.Request.Headers[VivRunDefine.SubjectIdHeader] = context.User.FindFirstValue(VivClaimTypes.TenantId) ?? "";
                    context.Request.Headers[VivRunDefine.ServiceNameHeader] = VivEngine.VivOptions?.EnvOption?.ServiceName ?? "";
                    context.Request.Headers[VivRunDefine.InnerRequestTokenHeader] = Power.RequestTokenAnalysisMagic.SignContextHeaders(context.Request.Headers);
                }

                await next();
            });

            app.MapReverseProxy(proxyPipeline =>
            {
                if (configureReverseProxy != null)
                {
                    configureReverseProxy(proxyPipeline);
                }
                else
                {
                    proxyPipeline.UseSessionAffinity();
                    proxyPipeline.UseLoadBalancing();
                    proxyPipeline.UsePassiveHealthChecks();
                }
            });

            configure?.Invoke(app);

            app.Run();
        }

        /// <summary>
        /// SignalR/WebSocket：浏览器无法在升级请求里设置 Authorization 头，SignalR 客户端把 token 放 access_token 查询参数。
        /// 网关在这里把它读出来交给 JwtBearer 认证；认证通过后再回填 x-viv-* 头给下游。
        /// </summary>
        private static void AddAccessTokenFromQuery(JwtBearerOptions options)
        {
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    if (string.IsNullOrEmpty(context.Token)
                        && context.HttpContext.Request.Query.TryGetValue("access_token", out var token))
                    {
                        context.Token = token.ToString();
                    }

                    return Task.CompletedTask;
                }
            };
        }

        /// <summary>
        /// 开发环境：让 YARP 下游 HttpClient 信任所有证书。
        /// </summary>
        private sealed class IgnoreSslErrorsFilter : IHttpMessageHandlerBuilderFilter
        {
            public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
            {
                return builder =>
                {
                    next(builder);

                    if (builder.PrimaryHandler is HttpClientHandler handler)
                    {
                        handler.ServerCertificateCustomValidationCallback = (a, b, c, d) => true;
                    }
                };
            }
        }
    }
}
