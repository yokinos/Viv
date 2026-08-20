using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Reflection;
using System.Text;
using Viv.Aoi;
using Viv.Contracts.Interface;
using Viv.Echo.Grpc;
using Viv.Engine.Filter;
using Viv.Engine.Middleware;
using Viv.Sandrone.Conveter;
using Viv.Sandrone.OpenApi;

namespace Viv.Engine
{
    public static class VivStartApiExtensions
    {
        private const string ApiTitleKey = "__VivApiTitle";
        private const string VivAuthRegisteredKey = "__VivAuthRegistered";
        private const string VivGrpcServerEnabledKey = "__VivGrpcServerEnabled";

        /// <summary>
        /// 配置 Viv API 基础服务：加载配置、Autofac 容器、AddViv、MVC、CORS、OpenAPI、编码注册。
        /// 需要先调用 builder.AddServiceDefaults()。
        /// </summary>
        /// <param name="configureMvc">注册额外 MVC 过滤器（默认已添加 VivExceptionFilterAttribute）</param>
        public static WebApplicationBuilder AddVivApi(this WebApplicationBuilder builder,
            string apiTitle,
            Action<MvcOptions>? configureMvc = null,
            Action<IServiceCollection>? serviceCollectionConfigure = null)
        {
            var vivOptions = VivEngine.LoadVivConfig(builder.Configuration);
            ArgumentNullException.ThrowIfNull(vivOptions);

            // 暂存标题供 RunVivApi 使用
            builder.Configuration[ApiTitleKey] = apiTitle;
            builder.Services.AddHttpContextAccessor();

            // Autofac 容器
            builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
            builder.Host.ConfigureContainer<ContainerBuilder>(container =>
            {
                container.VivAutofacRegister(vivOptions.DIOption);
            });

            if (vivOptions.LogOption != null && vivOptions.LogOption.LogType == Log.LogType.Serilog)
            {
                builder.Host.UseSerilog();
            }

            // 基础服务
            builder.Services.AddViv(vivOptions);

            // gRPC 服务端（配置驱动）：appsettings.json 的 VivOptions.EchoOption.GrpcOption.EnableServer 时，
            // 自动装配 Kestrel 专用端口（严格 HTTP/2）+ 自动发现注册 gRPC 服务，宿主零手工接线。
            // AddVivGrpcKestrel 内部已调 AddVivGrpcServer（含 VivGrpcServerInterceptor 租户上下文恢复），
            // 勿重复调用；EnableServer 标记经 Configuration 传给 RunVivApi 决定是否自动映射。
            var grpcOption = vivOptions.EchoOption?.GrpcOption;
            if (grpcOption is { EnableServer: true })
            {
                builder.AddVivGrpcKestrel(grpcOption.Port);
                VivGrpcDiscovery.RegisterServices(builder.Services);
                builder.Configuration[VivGrpcServerEnabledKey] = "true";
            }

            // 下游自鉴权：注册 JwtBearer（读 appsettings.json 的 VivOptions.TokenOption）。
            // 网关不强制鉴权，只解析透传 x-viv-* 上下文头；API 服务自己用 [Authorize] 控制。
            // TokenOption 为 null（如 hertalink）时跳过，保持匿名——此时 RunVivApi 不注册 UseAuthentication/UseAuthorization。
            var hasAuth = VivJwtBearerHelper.ConfigureJwtBearer(builder.Services, vivOptions.TokenOption, throwIfMissing: false);
            builder.Configuration[VivAuthRegisteredKey] = hasAuth ? "true" : "false";

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
                configureMvc?.Invoke(options);
            })
            .AddNewtonsoftJson(json =>
            {
                json.SerializerSettings.DateFormatString = "yyyy-MM-dd HH:mm:ss";
                json.SerializerSettings.ContractResolver = new VivContractResolver()
                {
                    NamingStrategy = new Newtonsoft.Json.Serialization.CamelCaseNamingStrategy()
                };
            });

            builder.Services.AddOpenApi(options =>
            {
                options.AddOperationTransformer<VivOpenApiOperationTransformer>();
                options.AddSchemaTransformer<VivOpenApiSchemaTransformer>();
            });

            // 跨域
            var corsPolicyName = Assembly.GetEntryAssembly()?.GetName().Name ?? "VivApi";
            builder.Services.AddCors(options =>
            {
                options.AddPolicy(corsPolicyName, policy =>
                {
                    policy.AllowAnyMethod().AllowAnyHeader().AllowAnyOrigin();
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
            var apiTitle = builder.Configuration[ApiTitleKey] ?? "Viv API";

            // TokenOption=null（匿名服务）时 ConfigureJwtBearer 未注册鉴权，此时不能调用 UseAuthentication/UseAuthorization，
            // 否则会抛 "Unable to resolve service for type 'IAuthenticationSchemeProvider'"。
            var hasAuth = builder.Configuration[VivAuthRegisteredKey] == "true";

            var app = builder.Build();
            VivLocator.Initialize(app.Services);

            // 网关代理场景：信任 YARP 默认透传的 X-Forwarded-Proto/Host/For，
            // 否则下游 UseHttpsRedirection 会把 http 请求 302 到自己的 https 地址，浏览器绕开网关直连下游。
            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
            });

            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi().AllowAnonymous();
                app.VivUseScalar(apiTitle);
            }

            app.UseMiddleware<HttpTrackMiddleware>();
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

            app.UseHttpsRedirection();
            if (hasAuth)
            {
                app.UseAuthorization();
            }
            app.MapControllers();

            // gRPC 服务端启用时自动映射发现的服务（配置驱动，宿主无需手动 MapGrpcService<T>）
            if (builder.Configuration[VivGrpcServerEnabledKey] == "true")
            {
                VivGrpcDiscovery.MapServices(app);
            }

            configure?.Invoke(app);

            app.Run();
        }
    }
}
