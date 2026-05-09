using Microsoft.AspNetCore.SignalR;
using Viv.Engine;
using Viv.Herta.Core.IService;
using Viv.Herta.Link.Hubs;
using Viv.Herta.Link.Services;

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
        builder.Services.AddSignalR()
            .AddStackExchangeRedis("localhost:6379,password=vivRedis");

        var app = builder.Build();

        // 初始化连接池
        var hubContext = app.Services.GetRequiredService<IHubContext<ChatHub>>();
        ConnectionPool.Initialize(hubContext);

        app.MapDefaultEndpoints();
        app.UseHttpsRedirection();

        app.MapHub<ChatHub>("/chat");

        app.Run();
    }
}
