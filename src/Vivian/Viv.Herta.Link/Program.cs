using Microsoft.Extensions.Options;
using Viv.Elysia.Filter;
using Viv.Engine;
using Viv.Herta.Link.Extensions;
using Viv.Herta.Link.Hubs;
using Viv.Herta.Link.Options;

namespace Viv.Herta.Link;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        builder.AddVivApi("Viv Herta Link", mvc => mvc.Filters.Add<RequestFilterAttribute>());
        // AddHertaLink：注册 ConnectionPool/GroupService + SignalR + Redis 背板（AddStackExchangeRedis）
        builder.Services.AddHertaLink(builder.Configuration);
        builder.RunVivApi(app =>
        {
            app.MapDefaultEndpoints();
            var linkOptions = app.Services.GetRequiredService<IOptions<HertaLinkOptions>>().Value;
            app.MapHub<ChatHub>(linkOptions.HubPath);
        });
    }
}
