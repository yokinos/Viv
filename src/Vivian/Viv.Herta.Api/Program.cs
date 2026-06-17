using Autofac;
using Autofac.Extensions.DependencyInjection;
using System.Text;
using Viv.Aoi;
using Viv.Echo.Grpc;
using Viv.Elysia.Filter;
using Viv.Engine;
using Viv.Engine.Conveter;
using Viv.Engine.Filter;
using Viv.Engine.Middleware;

namespace Viv.Herta.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();

        // 加载 Viv 配置
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

        // gRPC 客户端注册（Viv.Forge 编译时生成）
        if (vivOptions.EchoOption?.EnableGrpc == true)
        {
            builder.Services.AddVivSdkGrpcClients();
        }
        //builder.Services.AddOptions();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
        {
            // 关闭自动验证
            options.SuppressModelStateInvalidFilter = true;
        });

        // 控制器 + 全局异常 + JSON 格式化
        builder.Services.AddMvc(options =>
        {
            options.Filters.Add<VivExceptionFilterAttribute>();
            options.Filters.Add<RequestFilterAttribute>();
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
        builder.Services.AddCors(options =>
        {
            options.AddPolicy(typeof(Program).Namespace!, policy =>
            {
                policy.AllowAnyMethod().AllowAnyHeader().AllowAnyOrigin();
            });
        });

        builder.Services.AddSwagger(new Microsoft.OpenApi.OpenApiInfo()
        {
            Title = "Viv Herta API",
            Version = "1.0.0"
        });

        var app = builder.Build();
        app.MapDefaultEndpoints();
        VivLocator.Initialize(app.Services);

        // Swagger UI
        if (app.Environment.IsDevelopment())
        {
            app.VivUseSwagger(vivOptions.Env);
        }

        app.UseMiddleware<NotFoundMiddleware>();
        app.UseMiddleware<VivContextMiddleware>();

        app.UseStaticFiles();
        app.UseRouting();

        app.UseCors(typeof(Program).Namespace!);
        app.UseHttpsRedirection();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.Run();
    }
}
