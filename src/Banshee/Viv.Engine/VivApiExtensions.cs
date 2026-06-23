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
using Viv.Engine.Conveter;
using Viv.Engine.Filter;
using Viv.Engine.Middleware;

namespace Viv.Engine
{
    public static class VivApiExtensions
    {
        /// <summary>
        /// 配置 Viv API 基础服务：加载配置、Autofac 容器、AddViv、MVC、CORS、Swagger、编码注册。
        /// 需要先调用 builder.AddServiceDefaults()。
        /// </summary>
        /// <param name="configureMvc">注册额外 MVC 过滤器（默认已添加 VivExceptionFilterAttribute）</param>
        public static WebApplicationBuilder AddVivApi(
            this WebApplicationBuilder builder,
            string swaggerTitle,
            Action<MvcOptions>? configureMvc = null)
        {
            var vivOptions = VivEngine.LoadVivConfig();
            ArgumentNullException.ThrowIfNull(vivOptions);

            // Autofac 容器
            builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
            builder.Host.ConfigureContainer<ContainerBuilder>(container =>
            {
                container.VivAutofacRegister(vivOptions.DIOption);
            });

            // 基础服务
            builder.Services.AddViv(vivOptions);

            // gRPC 客户端（Viv.Forge 编译时生成，按需取消注释）
            if (vivOptions.EchoOption?.EnableGrpc == true)
            {
                //builder.Services.AddVivSdkGrpcClients();
            }

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
            {
                options.SuppressModelStateInvalidFilter = true;
            });

            // 控制器 + JSON 格式化
            builder.Services.AddMvc(options =>
            {
                options.Filters.Add<VivExceptionFilterAttribute>();
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

            // 跨域
            var corsPolicyName = Assembly.GetEntryAssembly()?.GetName().Name ?? "VivApi";
            builder.Services.AddCors(options =>
            {
                options.AddPolicy(corsPolicyName, policy =>
                {
                    policy.AllowAnyMethod().AllowAnyHeader().AllowAnyOrigin();
                });
            });

            builder.Services.AddSwagger(new OpenApiInfo
            {
                Title = swaggerTitle,
                Version = "1.0.0"
            });

            return builder;
        }

        /// <summary>
        /// Build → VivLocator → SwaggerUI → 中间件管道 → Run。
        /// 通过 configure 可在管道末尾（Run 之前）插入自定义中间件，如 app.UseTickerQ()、app.MapHub()。
        /// </summary>
        public static void RunVivApi(this WebApplicationBuilder builder, Action<WebApplication>? configure = null)
        {
            var vivOptions = VivEngine.LoadVivConfig();
            var corsPolicyName = Assembly.GetEntryAssembly()?.GetName().Name ?? "VivApi";

            var app = builder.Build();
            VivLocator.Initialize(app.Services);

            if (app.Environment.IsDevelopment() && vivOptions != null)
            {
                app.VivUseSwagger(vivOptions.Env);
            }

            app.UseMiddleware<NotFoundMiddleware>();
            app.UseMiddleware<VivContextMiddleware>();

            app.UseStaticFiles();
            app.UseRouting();

            app.UseCors(corsPolicyName);
            app.UseHttpsRedirection();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();

            configure?.Invoke(app);

            app.Run();
        }
    }
}
