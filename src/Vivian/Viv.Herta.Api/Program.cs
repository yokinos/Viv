using Viv.Elysia.Filter;
using Viv.Engine;

namespace Viv.Herta.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        // gRPC 服务端由 viv.config.json 的 EchoOption.GrpcOption 配置驱动（EnableServer + Port 7002）：
        // AddVivApi 自动装配 Kestrel 专用端口（严格 HTTP/2）+ 自动发现注册 gRPC 服务，
        // RunVivApi 自动映射，宿主零手工接线。
        builder.AddVivApi("Viv Herta API", mvc => mvc.Filters.Add<RequestFilterAttribute>());
        builder.RunVivApi(app => app.MapDefaultEndpoints());
    }
}
