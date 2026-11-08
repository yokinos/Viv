using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Viv.Kinna.Connections;
using Viv.Kinna.WebSockets;

namespace Viv.Kinna.Extensions
{
    /// <summary>
    /// 原生 WebSocket 的挂载点。与 <see cref="KinnaServiceCollectionExtensions.MapKinnaHub{THub}"/>
    /// 并列使用：两条路径指向同一套连接表，客户端可选其一。
    /// </summary>
    public static class KinnaWebSocketApplicationExtensions
    {
        /// <summary>
        /// 已经挂过 WebSocket 中间件的标记，避免多 Hub 时重复添加
        /// </summary>
        private const string WebSocketsEnabledKey = "Viv.Kinna.WebSockets.Enabled";

        /// <summary>
        /// 按本 Hub 的 <see cref="Options.KinnaOptions.WebSocketPath"/> 映射原生 WebSocket 端点。
        /// 路径为空表示不启用，直接返回，不影响只跑 SignalR 的部署。
        /// </summary>
        /// <param name="app">宿主</param>
        /// <param name="pattern">覆盖配置里的路径，一般不用传</param>
        public static WebApplication MapKinnaWebSocket<THub>(this WebApplication app, string? pattern = null)
            where THub : Hub
        {
            var session = app.Services.GetRequiredService<IKinnaSession<THub>>();
            var options = session.Options;
            var path = string.IsNullOrWhiteSpace(pattern) ? options.WebSocketPath : pattern;

            if (string.IsNullOrWhiteSpace(path))
            {
                return app;
            }

            // WebApplication 把 IApplicationBuilder.Properties 实现成显式的，这里要转一下
            var properties = ((IApplicationBuilder)app).Properties;

            if (!properties.ContainsKey(WebSocketsEnabledKey))
            {
                // 协议层 ping 用来发现 TCP 半开：客户端断电、拔网线时不会有关闭帧，
                // 只有 ping 失败才能让 Kestrel 把连接判死。业务心跳负责续租，两件事互不替代。
                app.UseWebSockets(new WebSocketOptions
                {
                    KeepAliveInterval = TimeSpan.FromSeconds(
                        options.WebSocketKeepAliveSeconds > 0 ? options.WebSocketKeepAliveSeconds : 30)
                });

                properties[WebSocketsEnabledKey] = true;
            }

            // 这里注册中间件晚于 MapControllers 也没关系：终结点由框架在管道末尾统一执行，
            // 只要 UseWebSockets 排在它前面就生效。
            app.Map(path, (RequestDelegate)(context => KinnaWebSocketEndpoint<THub>.HandleAsync(context)));

            // 停机时主动断掉本机的原生连接。不接这一步的话，滚动发布时客户端只能等
            // Kestrel 的连接关闭超时（默认 5 秒，反代后面可能更久）才重连到新副本——
            // 这段时间里推送发不到它，看着像「消息延迟」。
            // 每个 Hub 一份 registry，各注册各的，不需要去重。
            var registry = app.Services.GetRequiredService<KinnaWebSocketRegistry<THub>>();
            app.Services.GetRequiredService<IHostApplicationLifetime>()
                .ApplicationStopping.Register(registry.AbortAll);

            return app;
        }
    }
}
