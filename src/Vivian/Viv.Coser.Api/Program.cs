using TickerQ.DependencyInjection;
using Viv.Aspire.ServiceDefaults;
using Viv.Elysia.Filter;
using Viv.Engine;
using Viv.Echo.Proxies;

namespace Viv.Coser.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        builder.AddVivApi(mvc => mvc.Filters.Add<RequestFilterAttribute>());
        builder.Services.AddVivRpcProxies(builder.Configuration);
        builder.RunVivApi(app =>
        {
            app.MapDefaultEndpoints();
            app.UseTickerQ();
        });
    }
}
