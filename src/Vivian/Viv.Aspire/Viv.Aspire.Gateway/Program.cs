using Yarp.ReverseProxy.ServiceDiscovery;

namespace Viv.Aspire.Gateway;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // 1. 加载 YARP 配置
        builder.Configuration.AddJsonFile("viv.yarp.json", optional: false, reloadOnChange: true);

        // 2. 启用 Aspire 服务发现（这会让 YARP 能解析服务名）
        builder.AddServiceDefaults();

        // 3. 注册 YARP——LoadFromConfig 内部已支持服务发现
        builder.Services.AddReverseProxy()
            .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
            .AddServiceDiscoveryDestinationResolver();

        var app = builder.Build();

        // 4. 路由中间件（必须！否则代理不工作）
        app.UseRouting();

        app.MapReverseProxy();

        app.Run();
    }
}
