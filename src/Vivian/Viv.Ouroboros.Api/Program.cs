using Viv.Aspire.ServiceDefaults;
using Viv.Elysia.Extension;
using Viv.Engine;

namespace Viv.Ouroboros.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();

        // 不注入 AddElysiaFilter：它是操作日志那套（Request + OperationLog 两个过滤器），
        // 本项目暂时不需要。注：OperationLogFilterAttribute 对 IVivEventPublisher 的硬依赖已修成
        // 可选依赖（2026-10-06），所以现在即使注入它、且不配 NanaOption 也不会再 500 ——
        // 想开操作日志，把 mvc => mvc.Filters.AddElysiaFilter() 加回来即可。
        builder.AddVivApi(new ApiInitSetting("Viv Ouroboros API", "ouroboros"));

        builder.RunVivApi(app => app.MapDefaultEndpoints());
    }
}
