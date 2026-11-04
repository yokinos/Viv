using Viv.Aspire.ServiceDefaults;
using Viv.Elysia.Filter;
using Viv.Engine;
using Viv.Herta.Core.IService;
using Viv.Herta.Core.Service;
using Viv.ServiceProxy;

namespace Viv.Herta.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        builder.AddVivApi(mvc => mvc.Filters.Add<RequestFilterAttribute>());

        // 登录要校验 AppId：和连接准入问的是同一个查询（带 300 秒缓存）
        builder.Services.AddVivProxies(builder.Configuration);

        builder.RunVivApi(app => app.MapDefaultEndpoints());
    }
}
