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
        /// <param name="configure">
        /// 给终结点挂策略：<c>RequireAuthorization()</c>、<c>RequireRateLimiting("ws")</c>、
        /// <c>WithTags</c> 之类都要有 endpoint 元数据才生效。身份与准入仍然在
        /// <c>IKinnaIdentityResolver</c> / <c>IKinnaAdmission</c> 里判，
        /// 这里补的是宿主级的横切策略。
        /// </param>
        public static WebApplication MapKinnaWebSocket<THub>(
            this WebApplication app,
            string? pattern = null,
            Action<IEndpointConventionBuilder>? configure = null)
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

            var keepAlive = TimeSpan.FromSeconds(
                options.WebSocketKeepAliveSeconds > 0 ? options.WebSocketKeepAliveSeconds : 30);

            if (properties.TryGetValue(WebSocketsEnabledKey, out var applied))
            {
                // UseWebSockets 是进程级的，第二次调用不会覆盖间隔。与其静默沿用第一个 Hub 的值，
                // 不如启动就报错——「我明明配了 60 秒」这类问题不该留到线上才发现
                if (applied is TimeSpan existing && existing != keepAlive)
                {
                    throw new InvalidOperationException(
                        $"WebSocket 的 KeepAliveInterval 是进程级配置，多 Hub 只能有一个值：已生效 {existing}，本次 {keepAlive}");
                }
            }
            else
            {
                // 协议层 ping 用来发现 TCP 半开：客户端断电、拔网线时不会有关闭帧，
                // 只有 ping 失败才能让 Kestrel 把连接判死。业务心跳负责续租，两件事互不替代。
                app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = keepAlive });
                properties[WebSocketsEnabledKey] = keepAlive;
            }

            // 这里注册中间件晚于 MapControllers 也没关系：终结点由框架在管道末尾统一执行，
            // 只要 UseWebSockets 排在它前面就生效。
            var endpoint = app.Map(
                path,
                (RequestDelegate)(context => KinnaWebSocketEndpoint<THub>.HandleAsync(context)));

            configure?.Invoke(endpoint);

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
