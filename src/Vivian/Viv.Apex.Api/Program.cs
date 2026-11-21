using Viv.Aspire.ServiceDefaults;
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
        // 访问控制闸门的启动期校验（[VivRequireUserType] / [VivRequirePermission] 的参数与依赖）。
        // 拦截本身由特性自己完成，这里只买「配置写错就起不来」。
        builder.Services.AddVivApiGates();
        builder.AddVivApi(mvc => mvc.Filters.AddElysiaFilter());

        builder.RunVivApi(app =>
        {
            app.MapDefaultEndpoints();
        });
    }
}
