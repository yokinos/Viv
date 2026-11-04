using Viv.Aspire.ServiceDefaults;
using Viv.Echo;
using Viv.Echo.MagicOnion;
using Viv.Elysia.Extension;
using Viv.Elysia.Filter;
using Viv.Engine;

namespace Viv.Apex.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        builder.AddVivApi(mvc => mvc.Filters.AddElysiaFilter());

        // 服务端协议来自 VivOptions:EchoOption:GrpcOption:Protocol。
        // 端口由 AddVivApi 按 EffectiveProtocol 绑定（MagicOnion 与 gRPC 共用同一条 HTTP/2 端口），
        // 这里只在协议含 MagicOnion 时装配服务端过滤器与端点映射。
        var servesMagicOnion = VivEngine.VivOptions?.EchoOption?.GrpcOption?.ServesMagicOnion == true;
        if (servesMagicOnion)
        {
            builder.Services.AddVivMagicOnionServer();
        }

        builder.RunVivApi(app =>
        {
            app.MapDefaultEndpoints();

            if (servesMagicOnion)
            {
                app.MapVivMagicOnionServices();
            }
        });
    }
}
