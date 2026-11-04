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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Serilog;
using System;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Viv.Aoi;
using Viv.Contracts;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Engine.Middleware;
using Viv.Engine.Options;
using Viv.Engine.Power;
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
        internal const string CustomRateLimiterPolicyName = "CustomRateLimiter";
        private const int DefaultCacheSeconds = 20;
        private static readonly string[] _vivClaimTypes = ["subjectId", "userId", "appId"];

        /// <summary>
        /// 配置 Viv 网关基础服务：加载配置、限流配置热重载、Autofac、AddViv、JWT 解析、YARP（路由/集群从 Aspire 服务发现自动生成）、CORS、OutputCache、RateLimiter、编码注册。
        /// 需要先调用 builder.AddServiceDefaults()。
        /// </summary>
        /// <param name="serviceCollectionConfigure">追加网关服务</param>
        /// <param name="configureJwt">微调 JwtBearerOptions（events、挑战头等）</param>
        /// <param name="ignoreSslErrors">信任所有证书（仅开发环境应开启；默认 false，生产禁用避免 MITM）</param>
        /// <param name="rateLimitConfigFile">自定义限流策略配置，热重载</param>
        /// <param name="gatewayRouter">自定义网关路由提供程序</param>
        public static WebApplicationBuilder AddVivGateway(
            this WebApplicationBuilder builder,
            Action<IServiceCollection>? serviceCollectionConfigure = null,
            Action<JwtBearerOptions>? configureJwt = null,
            bool ignoreSslErrors = false,
            string rateLimitConfigFile = "viv.ratelimit.json",
            IGatewayRouteProvider? gatewayRouter = null)
        {
            // 绑定 + 写 VivEngine.VivOptions 快照 + 注册进 DI，三件事在这一次调用里做完
            var vivOptions = builder.AddVivConfig();

            // 限流配置热重载（路由/集群改为从 Aspire 服务发现自动生成，不再读 viv.yarp.json）
            builder.Configuration.AddJsonFile(rateLimitConfigFile, optional: false, reloadOnChange: true);

            // Autofac 容器
            builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
            builder.Host.ConfigureContainer<ContainerBuilder>(container =>
            {
                container.VivAutofacRegister(vivOptions);
            });

            builder.Services.AddViv(vivOptions);

            if (vivOptions.LogOption != null && vivOptions.LogOption.LogType == Log.LogType.Serilog)
            {
                builder.Logging.AddSerilog(dispose: false);
                builder.Logging.AddFilter<ConsoleLoggerProvider>(null, LogLevel.None);
            }

            builder.Services.AddHttpContextAccessor();
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // 开发环境忽略 SSL 校验
            if (ignoreSslErrors)
            {
                builder.Services.AddSingleton<IHttpMessageHandlerBuilderFilter, IgnoreSslErrorsFilter>();
            }

            // JWT 对称密钥解析（读 appsettings.json 的 VivOptions.TokenOption）——只解析，不强制，用于认证后透传 x-viv-* 上下文头
            // SignalR/WebSocket 升级请求无法带 Authorization 头，补充 access_token 查询参数认证（SignalR 标准约定）。
            var jwtConfigure = configureJwt == null ? (Action<JwtBearerOptions>)AddAccessTokenFromQuery : options => { AddAccessTokenFromQuery(options); configureJwt(options); };
            VivJwtBearerHelper.ConfigureJwtBearer(builder.Services, vivOptions.TokenOption, jwtConfigure, throwIfMissing: true);

            // YARP 反向代理：路由/集群从 Aspire 服务发现（services__* 环境变量）自动生成，零手写 JSON。
            // 短名优先取网关配置 VivGateway:ShortNames，没配才按服务名推断
            var gatewayOptions = builder.Configuration.GetSection(VivGatewayOptions.SectionName).Get<VivGatewayOptions>();
            var streamTimeoutSeconds = gatewayOptions?.StreamActivityTimeoutSeconds ?? 0;
            gatewayRouter ??= new VivGatewayRouteBuilder(
                gatewayOptions?.ShortNames,
                streamTimeoutSeconds > 0 ? TimeSpan.FromSeconds(streamTimeoutSeconds) : (TimeSpan?)null);
            var (gatewayRoutes, gatewayClusters) = gatewayRouter.Build();
            builder.Services.AddReverseProxy().LoadFromMemory(gatewayRoutes, gatewayClusters);

            // CORS：网关带凭证，必须显式 Origin。只按宿主环境判定，不看 VivOptions.EnvOption ——
            // 入库配置里 Env=Development，生产容器照搬就会把「任意 Origin + 凭证」带上去。
            var corsOrigins = vivOptions.CorsOption?.Origins;
            var corsIsDev = builder.Environment.IsDevelopment();
            builder.Services.AddCors(options =>
            {
                options.AddPolicy(DefaultCorsPolicyName, policy =>
                {
                    VivCors.Apply(policy, corsOrigins, corsIsDev, allowCredentials: true);
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
            // 读 IOptionsMonitor：AddJsonFile(reloadOnChange: true) 改了文件后，下一次请求就用新值。
            // 之前的写法在启动时把值拷进 lambda，热重载配置改了也不生效。
            builder.Services.Configure<VivRateLimitOptions>(builder.Configuration.GetSection(VivRateLimitOptions.CustomRateLimit));
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                // 只有这一个策略，所有自动生成的路由（api / docs / ws）都挂它。
                // 以前还注册过一个写死 4 次/12 秒的 DefaultRateLimiter，但没有任何路由引用 —— 死配置，删掉。
                options.AddPolicy(CustomRateLimiterPolicyName, context =>
                {
                    var current = context.RequestServices
                        .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<VivRateLimitOptions>>()
                        .CurrentValue;
                    return BuildPartition(context, current);
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

            // Aspire 入口 / 前置 LB 通常不在 loopback；dev 信任转发头，非 dev 只认 TrustedProxies。
            app.UseForwardedHeaders(VivForwardedHeaders.Create(VivEngine.VivOptions?.EnvOption, app.Environment.IsDevelopment()));

            app.UseMiddleware<RequestTrackMiddleware>();
            app.UseMiddleware<ApiStartedMiddleware>();

            app.UseWebSockets(new WebSocketOptions
            {
                KeepAliveInterval = webSocketKeepAliveInterval ?? TimeSpan.FromSeconds(15)
            });

            // 开发环境：浏览器用局域网 IP / 主机名打开前端时，调本机网关属于 Private Network Access，
            // Chrome 的预检会要求显式回 Access-Control-Allow-Private-Network，否则按跨域拦截。
            var corsIsDev = builder.Environment.IsDevelopment();
            if (corsIsDev)
            {
                app.Use(async (context, next) =>
                {
                    if (string.Equals(context.Request.Headers["Access-Control-Request-Private-Network"], "true", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
                    }

                    await next();
                });
            }

            app.UseCors(DefaultCorsPolicyName);
            app.UseRateLimiter();

            app.UseAuthentication();
            app.UseAuthorization();

            // OutputCache 放在认证之后：策略要按身份区分时（VaryByHeader / VaryByValue）才拿得到
            // 已认证的 User。放在认证前缓存键里没有身份，一旦真挂上策略就会跨用户串响应。
            // 注：策略注册了但没有任何端点调用 .CacheOutput()，需要缓存的端点自己显式挂。
            app.UseOutputCache();

            // 认证后把用户信息透传给下游（claims 仅在认证后可用）。
            // Header 契约与 RequestTokenResolver 对齐：
            //   x-viv-appId / x-viv-subjectId(=SubjectId) / x-viv-userId / x-viv-serviceName / x-viv-holder-id
            // 先剥离客户端可能伪造的 x-viv-* 上下文头与 x-request-token，只回填来自验签 token 的值 + 网关当前 holderId。
            // 回填后对头组做 HMAC-SHA256 签名写入 x-request-token，下游验签通过才信任——防止绕过网关直连下游伪造头。
            app.Use(async (context, next) =>
            {
                foreach (var header in new[]
                {
                    VivRunDefine.AppIdHeader,
                    VivRunDefine.SubjectIdHeader,
                    VivRunDefine.UserIdHeader,
                    VivRunDefine.ServiceNameHeader,
                    VivRunDefine.HolderIdHeader,
                    VivRunDefine.InnerRequestTokenHeader
                })
                {
                    context.Request.Headers.Remove(header);
                }

                // 剥离客户端可伪造的身份 query 参数（subjectId/userId/appId）：
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
                    context.Request.Headers[VivRunDefine.SubjectIdHeader] = context.User.FindFirstValue(VivClaimTypes.SubjectId) ?? "";
                    context.Request.Headers[VivRunDefine.ServiceNameHeader] = VivEngine.VivOptions?.EnvOption?.ServiceName ?? "";
                    context.Request.Headers[VivRunDefine.HolderIdHeader] = LockHolderContext.CurrentHolderId;
                    context.Request.Headers[VivRunDefine.InnerRequestTokenHeader] = Power.RequestTokenResolver.SignContextHeaders(context.Request.Headers);
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
        /// 按客户端 IP 分区计数。原实现用 <c>AddFixedWindowLimiter(policy)</c>，那是「全网关共用一个桶」：
        /// PermitLimit 是整个进程的总量，一个客户端就能把其他所有人挤掉。分区后每个来源各算各的。
        /// 分区键里带上当前配置值，热重载改了配额会自动切到新分区，不必重启。
        /// </summary>
        private static RateLimitPartition<string> BuildPartition(HttpContext context, VivRateLimitOptions options)
        {
            // UseForwardedHeaders 已把 RemoteIpAddress 换成真实客户端 IP
            var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            // 约定：PermitLimit <= 0 表示不限流
            if (options.PermitLimit <= 0)
            {
                return RateLimitPartition.GetNoLimiter(client);
            }

            var key = client + "|"
                      + options.PermitLimit.ToString(CultureInfo.InvariantCulture) + "|"
                      + options.Window.ToString(CultureInfo.InvariantCulture) + "|"
                      + options.QueueLimit.ToString(CultureInfo.InvariantCulture);

            return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = options.PermitLimit,
                Window = TimeSpan.FromSeconds(Math.Max(1, options.Window)),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = Math.Max(0, options.QueueLimit)
            });
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
                    // 只对 /ws/{短名}/** 生效。query 里的 token 会进访问日志 / Referer / 浏览器历史，
                    // 不该让普通 API 也能用这条通道 —— 否则等于给所有 REST 端点开了个明文传输后门。
                    if (string.IsNullOrEmpty(context.Token)
                        && context.HttpContext.Request.Path.StartsWithSegments("/ws")
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
