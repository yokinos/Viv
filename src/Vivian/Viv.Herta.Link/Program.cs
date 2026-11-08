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
        builder.AddVivApi(mvc => mvc.Filters.Add<RequestFilterAttribute>());
        builder.Services.AddHertaLink(builder.Configuration);
        builder.RunVivApi(app =>
        {
            app.MapDefaultEndpoints();
            app.MapKinnaHub<ChatHub>();

            // 原生 WebSocket：小程序、嵌入式设备这类引不进 SignalR 客户端的场景走这条。
            // 与上面的 Hub 共用连接表和投递器，同一个人从两端上线会各收一份。
            app.MapKinnaWebSocket<ChatHub>();
        });
    }
}
