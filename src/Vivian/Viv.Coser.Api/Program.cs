using TickerQ.DependencyInjection;
using Viv.Aspire.ServiceDefaults;
using Viv.Elysia.Filter;
using Viv.Engine;
using Viv.ServiceProxy.Common;

namespace Viv.Coser.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        builder.AddVivApi(mvc => mvc.Filters.Add<RequestFilterAttribute>());

        // 定时任务要遍历客户端应用，数据在 Apex：注册 gRPC 查询（带缓存）
        builder.Services.AddVivProxies(builder.Configuration);

        builder.RunVivApi(app =>
        {
            app.MapDefaultEndpoints();
            app.UseTickerQ();
        });
    }
}
