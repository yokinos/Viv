using Microsoft.Extensions.Options;
using Viv.Elysia.Filter;
using Viv.Engine;
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
        builder.Services.AddSignalR();
        builder.RunVivApi(app =>
        {
            app.MapDefaultEndpoints();
            var linkOptions = app.Services.GetRequiredService<IOptions<HertaLinkOptions>>().Value;
            app.MapHub<ChatHub>(linkOptions.HubPath);
        });
    }
}
