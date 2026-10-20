using Microsoft.Extensions.Options;
using Viv.Aspire.ServiceDefaults;
using Viv.Elysia.Filter;
using Viv.Engine;
using Viv.Herta.Link.Extensions;
using Viv.Herta.Link.Hubs;
using Viv.Herta.Link.Options;
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
        // AddHertaLink：注册 ConnectionPool / GroupService / 投递链路 + SignalR（刻意不装 Redis 背板，见扩展方法注释）
        builder.Services.AddHertaLink(builder.Configuration);
        builder.RunVivApi(app =>
        {
            app.MapDefaultEndpoints();
            var linkOptions = app.Services.GetRequiredService<IOptions<HertaLinkOptions>>().Value;
            app.MapHub<ChatHub>(linkOptions.HubPath);

            // 注册期拿不到 logger，广播队列名只能在这里打；两个副本队列名相同就是实例标识撞了。
            var logger = app.Services.GetRequiredService<ILoggerContract>();
            if (NanaRegister.BroadcastQueues.Count == 0)
            {
                logger.Info("没有声明实例广播队列（所有消费者都用「同服务共享一条队列」拓扑）");
            }
            else
            {
                logger.Info($"实例广播队列（InstanceId = {NanaRegister.InstanceId}）: {string.Join(", ", NanaRegister.BroadcastQueues)}");
            }
        });
    }
}
