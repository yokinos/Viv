using Microsoft.Extensions.Options;
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

        var vivOptions = VivEngine.LoadVivConfig();
        ArgumentNullException.ThrowIfNull(vivOptions);

        builder.Services.AddViv(vivOptions);
        builder.Services.AddHertaLink(builder.Configuration);

        var app = builder.Build();

        app.MapDefaultEndpoints();
        app.UseHttpsRedirection();

        var linkOptions = app.Services.GetRequiredService<IOptions<HertaLinkOptions>>().Value;
        app.MapHub<ChatHub>(linkOptions.HubPath);

        app.Run();
    }
}
