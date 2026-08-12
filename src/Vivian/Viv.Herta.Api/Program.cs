using Viv.Echo.Grpc;
using Viv.Elysia.Filter;
using Viv.Engine;
using Viv.ServiceProxy.Examples;

namespace Viv.Herta.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        // 专用 gRPC 端口 7002（严格 HTTP/2），REST 端口沿用 urls（HTTP/1.1）——明文下 REST 与 gRPC 无法同端口共存
        // AddVivGrpcKestrel 声明端口即自动装配服务端（含 VivGrpcServerInterceptor 租户上下文恢复拦截器）
        builder.AddVivGrpcKestrel(7002);
        builder.AddVivApi("Viv Herta API", mvc => mvc.Filters.Add<RequestFilterAttribute>());

        builder.Services.AddScoped<TenantGrpcService>();

        builder.RunVivApi(app =>
        {
            app.MapDefaultEndpoints();
            // 框架示例服务作 gRPC 宿主验证：4 个 RPC 覆盖 unary / server-streaming / client-streaming / bidi
            app.MapGrpcService<TenantGrpcService>();
        });
    }
}
