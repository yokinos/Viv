using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.IdentityModel.Tokens;
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
        private const string DefaultRateLimiterPolicyName = "DefaultRateLimiter";
        private const string CustomRateLimiterPolicyName = "CustomRateLimiter";
        private const string ReverseProxySectionName = "ReverseProxy";
        private const int DefaultCacheSeconds = 20;

        /// <summary>
        /// 配置 Viv 网关基础服务：加载配置、YARP/限流配置热重载、Autofac、AddViv、JWT 验证、YARP、CORS、OutputCache、RateLimiter、编码注册。
        /// 需要先调用 builder.AddServiceDefaults()。
        /// </summary>
        /// <param name="serviceCollectionConfigure">追加网关服务</param>
        /// <param name="configureJwt">微调 JwtBearerOptions（events、挑战头等）</param>
        /// <param name="ignoreSslErrors">开发环境信任所有证书（YARP 下游有 https://localhost 自签名地址时启用）</param>
        /// <param name="yarpConfigFile">YARP 路由/集群配置，热重载</param>
        /// <param name="rateLimitConfigFile">自定义限流策略配置，热重载</param>
        public static WebApplicationBuilder AddVivGateway(
            this WebApplicationBuilder builder,
            Action<IServiceCollection>? serviceCollectionConfigure = null,
            Action<JwtBearerOptions>? configureJwt = null,
            bool ignoreSslErrors = true,
            string yarpConfigFile = "viv.yarp.json",
            string rateLimitConfigFile = "viv.ratelimit.json")
        {
            var vivOptions = VivEngine.LoadVivConfig();
            ArgumentNullException.ThrowIfNull(vivOptions);

            // YARP / 限流配置热重载
            builder.Configuration.AddJsonFile(yarpConfigFile, optional: false, reloadOnChange: true);
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

            // JWT 对称密钥验证（读 viv.config.json 的 TokenOption）
            ConfigureJwtBearer(builder, vivOptions, configureJwt);

            // YARP 反向代理
            builder.Services.AddReverseProxy()
                .LoadFromConfig(builder.Configuration.GetSection(ReverseProxySectionName));

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
        /// Build → VivLocator → WebSocket → CORS → OutputCache → RateLimiter → 认证授权 → 用户信息注入头 → YARP → configure → Run。
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
            // 先剥离客户端可能伪造的 x-viv-* 上下文头，只回填来自验签 token 的值。
            app.Use(async (context, next) =>
            {
                foreach (var header in new[]
                {
                    Power.RequestTokenAnalysisMagic.AppIdHeader,
                    Power.RequestTokenAnalysisMagic.SubjectIdHeader,
                    Power.RequestTokenAnalysisMagic.UserIdHeader,
                    Power.RequestTokenAnalysisMagic.ServiceNameHeader
                })
                {
                    context.Request.Headers.Remove(header);
                }

                if (context.User.Identity?.IsAuthenticated == true)
                {
                    // .NET 10 JwtBearer 只映射 sub → NameIdentifier（name 保持短格式），两种都兼容。
                    var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? "";
                    context.Request.Headers[Power.RequestTokenAnalysisMagic.UserIdHeader] = userId;
                    context.Request.Headers[Power.RequestTokenAnalysisMagic.AppIdHeader] = context.User.FindFirstValue(VivClaimTypes.AppId) ?? "";
                    context.Request.Headers[Power.RequestTokenAnalysisMagic.SubjectIdHeader] = context.User.FindFirstValue(VivClaimTypes.TenantId) ?? "";
                    context.Request.Headers[Power.RequestTokenAnalysisMagic.ServiceNameHeader] = VivEngine.VivOptions?.EnvOption?.ServiceName ?? "";
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
        /// 配置 JwtBearer 对称密钥验证，密钥/发行方/受众来自 viv.config.json 的 TokenOption。
        /// </summary>
        private static void ConfigureJwtBearer(WebApplicationBuilder builder, VivOptions vivOptions, Action<JwtBearerOptions>? configureJwt)
        {
            var tokenOptions = vivOptions.TokenOption;
            if (tokenOptions == null || string.IsNullOrWhiteSpace(tokenOptions.SecretKey))
            {
                throw new InvalidOperationException("Viv 网关需要配置 viv.config.json 的 TokenOption 节点（SecretKey/Issuer/Audience），用于 JwtBearer 对称密钥验证。");
            }

            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(tokenOptions.SecretKey));
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.RequireHttpsMetadata = false;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = tokenOptions.Issuer,
                        ValidateAudience = true,
                        ValidAudience = tokenOptions.Audience,
                        ValidateLifetime = true,
                        IssuerSigningKey = signingKey,
                        ClockSkew = TimeSpan.Zero // 关闭时钟偏移容错，严格校验过期时间
                    };
                    configureJwt?.Invoke(options);
                });

            builder.Services.AddAuthorization();
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
