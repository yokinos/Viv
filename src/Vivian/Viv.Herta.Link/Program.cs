using Viv.Aspire.ServiceDefaults;
using Viv.Elysia.Filter;
using Viv.Engine;
using Viv.Herta.Link.Extensions;
using Viv.Herta.Link.Hubs;
using Viv.Kinna.Extensions;
using Viv.Log;
using Viv.Nana;

namespace Viv.Herta.Link;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        builder.AddVivApi(new ApiInitSetting("Viv Herta Link", "hertalink"), mvc => mvc.Filters.Add<RequestFilterAttribute>());
        builder.Services.AddHertaLink(builder.Configuration);
        builder.RunVivApi(app =>
        {
            app.MapDefaultEndpoints();
            app.MapKinnaHub<ChatHub>();
        });
    }
}
