using Viv.Aspire.Gateway.Magic;
using Yarp.ReverseProxy.ServiceDiscovery;

namespace Viv.Aspire.Gateway;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // 加载 YARP 配置
        builder.Configuration.AddJsonFile("viv.yarp.json", optional: false, reloadOnChange: true);

        // 启用 Aspire 服务发现（这会让 YARP 能解析服务名）
        builder.AddServiceDefaults();
        builder.Services.AddAllHttpClientsIgnoreSslErrors();

        // 注册 YARP——LoadFromConfig 内部已支持服务发现
        builder.Services.AddReverseProxy()
             .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
        
        
        
        
        
        var app = builder.Build();
        app.MapReverseProxy();

        app.Run();
    }
}
