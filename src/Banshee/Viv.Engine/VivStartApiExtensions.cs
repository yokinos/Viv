using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;
using System.Reflection;
using System.Text;
using Viv.Aoi;
using Viv.Engine.Filter;
using Viv.Engine.LocalEvents;
using Viv.Engine.Middleware;
using Viv.Engine.Options;
using Viv.Engine.Power;
using Viv.Keke;
using Viv.Keke.Grpc;
using Viv.Keke.MagicOnion;
using Viv.Sandrone.Conveter;
using Viv.Sandrone.OpenApi;

namespace Viv.Engine
{
    public static class VivStartApiExtensions
    {
        /// <summary>
        /// Scalar 文档标题：<c>DisplayName</c> 优先，回落 <c>ServiceName</c>。
        /// </summary>
        private static string ResolveApiTitle(EnvOptions? envOption)
        {
            if (!string.IsNullOrWhiteSpace(envOption?.DisplayName)) return envOption.DisplayName;
            return envOption?.ServiceName ?? string.Empty;
        }

        private static bool IsGrpcServerEnabled(GrpcOptions? grpcOption) => grpcOption is { Protocol: not KekeProtocol.None, ServesGrpc: true };

        private static bool IsMagicOnionEnabled(GrpcOptions? grpcOption) => grpcOption is { Protocol: not KekeProtocol.None, ServesMagicOnion: true };

        /// <summary>
        /// 配置 Viv API 基础服务：加载配置、Autofac 容器、AddViv、MVC、CORS、OpenAPI、编码注册。
        /// 需要先调用 builder.AddServiceDefaults()。
        /// </summary>
        /// <param name="configureMvc">注册额外 MVC 过滤器（默认已添加 VivExceptionFilterAttribute）</param>
        public static WebApplicationBuilder AddVivApi(this WebApplicationBuilder builder, Action<MvcOptions>? configureMvc = null, Action<IServiceCollection>? serviceCollectionConfigure = null)
        {
            // 绑定 + 写 VivEngine.VivOptions 快照 + 注册进 DI，三件事在这一次调用里做完
            var vivOptions = builder.AddVivConfig();
            var envOption = vivOptions.EnvOption;

            // 标题与网关前缀都来自配置（EnvOption.DisplayName / ShortName），宿主代码不再传名字。
            builder.Services.AddHttpContextAccessor();

            // Autofac 容器
            builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
            builder.Host.ConfigureContainer<ContainerBuilder>(container =>
            {
                container.VivAutofacRegister(vivOptions);
            });

            builder.Logging.AttachVivLogging(vivOptions.LogOption);

            // 基础服务
            builder.Services.AddViv(vivOptions);

            // gRPC / MagicOnion 服务端（配置驱动）：VivOptions.KekeOption.GrpcOption 的端口由两种传输共用，
            // Protocol 决定这一进程要注册哪些服务端实现：
            //   Grpc       → 只注册 gRPC 服务端（拦截器 + 自动发现 + MapGrpcService）
            //   MagicOnion → 只绑端口，服务端 DI 半段由 KekeRegister 装配，端点映射在本方法末尾自动补
            //   Both       → 两边都注册（迁移期）
            //   None       → 不占端口（GrpcOption 节点配了但协议为 None 等同于没开）
            // 两边都只在这里判一次协议，宿主不再自己读 KekeOption 决定装不装 MagicOnion。
            var grpcOption = vivOptions.KekeOption?.GrpcOption;
            if (grpcOption is { Protocol: not KekeProtocol.None })
            {
                builder.AddVivGrpcKestrel(grpcOption.Port, grpcOption.ServesGrpc);

                if (IsGrpcServerEnabled(grpcOption))
                {
                    VivGrpcDiscovery.RegisterServices(builder.Services);
                }
            }

            // 下游自鉴权：注册 JwtBearer（读 appsettings.json 的 VivOptions.TokenOption）。
            // 网关不强制鉴权，只解析透传 x-viv-* 上下文头；API 服务自己用 [Authorize] 控制。
            // TokenOption 为 null（如 hertalink）时跳过，保持匿名——此时 RunVivApi 不注册 UseAuthentication/UseAuthorization。
            VivJwtBearerHelper.ConfigureJwtBearer(builder.Services, vivOptions.TokenOption, throwIfMissing: false);

            serviceCollectionConfigure?.Invoke(builder.Services);
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
            {
                options.SuppressModelStateInvalidFilter = true;
            });

            // 控制器 + JSON 格式化
            builder.Services.AddMvc(options =>
            {
                options.Filters.Add<VivExceptionFilterAttribute>();
                options.Filters.Add<VivApiResultFilterAttribute>();
                // 本地事件分发触发点（HTTP 主路径）：next() 返回后按业务成败 Flush/Discard。
                // 必须排在 VivExceptionFilterAttribute 之后 —— 后者是外层，异常经它处理后
                // 仍会回到本过滤器续段，此时靠 context.Result 里的错误信封判成败。
                options.Filters.Add<LocalEventFlushFilterAttribute>();
                configureMvc?.Invoke(options);
            })
            .AddNewtonsoftJson(json =>
            {
                // 时间一律 ISO 8601 + UTC 后缀，和 VivApiResult / SSE 走同一套规则，
                // 否则控制器返回和信封返回的时间格式会不一致（前端得写两套解析）。
                JsonNetSetting.ApplyDateTimeRule(json.SerializerSettings);
                json.SerializerSettings.ContractResolver = new VivContractResolver()
                {
                    NamingStrategy = new Newtonsoft.Json.Serialization.CamelCaseNamingStrategy()
                };
            });

            builder.Services.AddOpenApi(options =>
            {
                options.AddOperationTransformer<VivOpenApiOperationTransformer>();
                options.AddSchemaTransformer<VivOpenApiSchemaTransformer>();
                if (!string.IsNullOrWhiteSpace(envOption?.ShortName))
                {
                    var shortName = envOption.ShortName;
                    options.AddDocumentTransformer((document, context, cancellationToken) =>
                    {
                        document.Servers =
                        [
                            new OpenApiServer { Url = shortName, Description = "API Gateway" }
                        ];
                        return Task.CompletedTask;
                    });
                }
            });

            // 跨域：只按宿主环境判定，不看 VivOptions.EnvOption —— 入库的 appsettings.json 里 Env 写的是
            // Development，生产容器照搬配置就会把「dev 放行任意 Origin」带上去。未配置 Origins 时：
            // Development 回显任意 Origin（局域网 IP 访问 dev server 需要），其余环境不放行任何跨域来源。
            var corsPolicyName = Assembly.GetEntryAssembly()?.GetName().Name ?? "VivApi";
            var corsOrigins = vivOptions.CorsOption?.Origins;
            var corsIsDev = builder.Environment.IsDevelopment();
            builder.Services.AddCors(options =>
            {
                options.AddPolicy(corsPolicyName, policy =>
                {
                    VivCors.Apply(policy, corsOrigins, corsIsDev, allowCredentials: false);
                });
            });

            return builder;
        }

        /// <summary>
        /// Build → VivLocator → Scalar → 中间件管道 → Run。
        /// 通过 configure 可在管道末尾（Run 之前）插入自定义中间件，如 app.UseTickerQ()、app.MapHub()。
        /// </summary>
        public static void RunVivApi(this WebApplicationBuilder builder, Action<WebApplication>? configure = null)
        {
            // 配置已在 AddVivApi 加载过一次并写入 VivEngine.VivOptions，这里不重复读（原二次读取是死变量）
            var corsPolicyName = Assembly.GetEntryAssembly()?.GetName().Name ?? "VivApi";

            // 标题、鉴权开关、gRPC/MagicOnion 端点都在这里现算，不从配置里读中间键 ——
            // 那种键会参与环境变量与命令行覆盖链：把鉴权标志顶成 "true"，就会在没有 JwtBearer
            // 注册的情况下调 UseAuthentication，启动直接崩。
            var vivOptions = VivEngine.VivOptions;
            var apiTitle = ResolveApiTitle(vivOptions?.EnvOption);
            if (string.IsNullOrWhiteSpace(apiTitle)) apiTitle = "Viv API";

            // TokenOption=null（匿名服务）时 ConfigureJwtBearer 未注册鉴权，此时不能调用 UseAuthentication/UseAuthorization，
            // 否则会抛 "Unable to resolve service for type 'IAuthenticationSchemeProvider'"。
            // CanRegister 与 AddVivApi 里的注册调用共用同一个判定，不会出现「说装了但没装」。
            var hasAuth = VivJwtBearerHelper.CanRegister(vivOptions?.TokenOption);

            var app = builder.Build();
            VivLocator.Initialize(app.Services);

            // 网关代理场景：信任 YARP 透传的 X-Forwarded-Proto/Host/For。
            // Aspire/YARP 网关不在 loopback，dev 下清空默认 KnownProxies；非 dev 只认 TrustedProxies，
            // 没配就退回 loopback 默认，避免任意来源伪造 X-Forwarded-For。
            app.UseForwardedHeaders(VivForwardedHeaders.Create(vivOptions?.EnvOption, app.Environment.IsDevelopment()));

            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi().AllowAnonymous();
                app.VivUseScalar(apiTitle);
            }

            app.UseMiddleware<RequestTrackMiddleware>();
            app.UseMiddleware<ApiStartedMiddleware>();

            app.UseStaticFiles();
            app.UseRouting();

            // CORS 先于鉴权，避免预检(OPTIONS)被上下文中间件短路
            app.UseCors(corsPolicyName);

            // JWT 只验证一次：UseAuthentication(JwtBearer) 先跑并填充 HttpContext.User，
            // VivContextMiddleware 直接从已验证的 principal 读取上下文，不再二次验签。
            // 匿名服务（TokenOption=null）未注册鉴权，跳过 UseAuthentication/UseAuthorization 以免启动崩溃。
            if (hasAuth)
            {
                app.UseAuthentication();
            }

            app.UseMiddleware<VivContextMiddleware>();

            // 本地事件分发兜底（非 MVC 端点：gRPC 服务 / SignalR hub / health）。
            // 位置有两个讲究：① 必须在 VivContextMiddleware 之内 —— 分发要跑在它 finally Clear()
            // 租户上下文之前，否则处理器拿不到 IVivContext（租户过滤会失效）；
            // ② 必须在 MVC 之外包住它 —— Flush/Discard 幂等，MVC 已处理过的请求到这里是 no-op。
            // TickerQ / 手写 BackgroundService 不走这条管道，请用 IVivLocalEventScope.RunAsync。
            app.UseMiddleware<LocalEventFlushMiddleware>();

            // 默认不跳转：TLS 在网关 / 边缘终止，下游自己跳转会把浏览器 307 到服务自身的 https 端口
            // （如 https://localhost:7093），请求逃出网关且丢掉 /{服务短名} 前缀 —— 与 VivForwardedHeaders
            // 注释里那个坑是同一个。只有服务被直接以 http 暴露时才在 appsettings.json 打开。
            if (vivOptions?.EnvOption?.EnableHttpsRedirection == true)
            {
                app.UseHttpsRedirection();
            }
            if (hasAuth)
            {
                app.UseAuthorization();
            }
            app.MapControllers();

            // gRPC / MagicOnion 端点映射：与 AddVivApi 里的注册调同一组判定函数，条件只有一份，
            // 不会出现「注册了却没 Map」或反过来。
            var grpcOption = vivOptions?.KekeOption?.GrpcOption;
            if (IsGrpcServerEnabled(grpcOption))
            {
                VivGrpcDiscovery.MapServices(app);
            }

            // MagicOnion 服务端同理：DI 已在 KekeRegister 装好，这里只补端点映射（必须在 Build 之后）
            if (IsMagicOnionEnabled(grpcOption))
            {
                app.MapVivMagicOnionServices();
            }

            configure?.Invoke(app);

            app.Run();
        }
    }
}
