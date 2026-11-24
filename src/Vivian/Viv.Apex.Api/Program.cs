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

        // 操作日志：默认走消息队列（UserOperationLogEvent → Worker 消费落库）。
        // 接入自有的 IOperationLogWriter 后可把 UseEventPublisher 关掉，避免两边各写一份。
        builder.Services.AddVivOperationLog(o =>
        {
            o.RecordFailure = true;    // 失败登录等尝试必须留痕，这是审计的主场景
            o.IncludeResponse = false; // 登录响应里就是 AccessToken，体积和敏感度都不划算
        });

        builder.RunVivApi(app =>
        {
            app.MapDefaultEndpoints();
        });
    }
}
