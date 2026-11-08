using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Viv.Kinna.Connections;
using Viv.Kinna.Contracts;
using Viv.Kinna.Dispatch;
using Viv.Kinna.Health;
using Viv.Kinna.Options;
using Viv.Kinna.Ports;
using Viv.Kinna.WebSockets;

namespace Viv.Kinna.Extensions
{
    /// <summary>
    /// 长连接的接入点。具体 Hub 只出现在 AddKinna&lt;THub&gt; 和 MapKinnaHub&lt;THub&gt;。
    /// 共享配置由 AddViv 绑进 DI（VivOptions:KinnaOption），这里补每个 Hub 的会话；
    /// 单 Hub 覆盖写 VivOptions:KinnaOption:{Hub 类型名}。
    /// </summary>
    public static class KinnaServiceCollectionExtensions
    {
        /// <param name="services">宿主容器</param>
        /// <param name="configure">代码里覆盖，多 Hub 时各给各的 HubPath 和 Prefix</param>
        public static IServiceCollection AddKinna<THub>(this IServiceCollection services, Action<KinnaOptions>? configure = null) where THub : Hub
        {
            // 幂等；EnableDetailedErrors 是进程级选项，由 AddViv 按 KinnaOption 配一次
            services.AddSignalR();

            // 时间源全进程一份：连接池、清扫器、健康检查都从这里取。
            // 用 TryAdd 而不是 Add，多 Hub 重复调用也只会有一个实例，测试里换成 FakeTimeProvider 就能整体推进
            services.TryAddSingleton<TimeProvider>(TimeProvider.System);

            // 配置只组装一次：会话、连接表、发送器三个单例共用，
            // 否则 configure 回调会被执行三遍
            services.TryAddSingleton<KinnaHubSetup<THub>>(sp =>
                new KinnaHubSetup<THub>(BuildOptions(sp, typeof(THub).Name, configure)));

            // 准入默认只校验身份完整性。业务注册自己的实现即可覆盖（后注册的生效），
            // 两套传输共用同一个端口，不会出现换客户端就绕过限制的情况
            services.TryAddScoped<IKinnaAdmission<THub>, KinnaIdentityAdmission<THub>>();

            // 原生 WebSocket 的连接表与组表。没配 WebSocketPath 时也会建出来，
            // 只是没人往里登记，发送器分流时全部落到 SignalR 那一侧
            services.TryAddSingleton<KinnaWebSocketRegistry<THub>>(sp =>
                new KinnaWebSocketRegistry<THub>(
                    sp.GetRequiredService<KinnaHubSetup<THub>>().Options.WebSocketQueueCapacity));

            services.TryAddSingleton<IKinnaSession<THub>>(sp =>
            {
                var options = sp.GetRequiredService<KinnaHubSetup<THub>>().Options;

                return new KinnaSession<THub>(
                    new KinnaConnectionPool(typeof(THub).Name, sp.GetService<TimeProvider>()),
                    new CompositeKinnaClientSender<THub>(
                        sp.GetRequiredService<KinnaWebSocketRegistry<THub>>(),
                        new KinnaClientSender<THub>(sp.GetRequiredService<IHubContext<THub>>())),
                    new KinnaKeys(options.Prefix),
                    options);
            });

            services.TryAddSingleton<KinnaSweepClock<THub>>();
            services.TryAddSingleton<KinnaUplinkLimiter<THub>>();

            services.TryAddScoped<IKinnaConnectionCoordinator<THub>, KinnaConnectionCoordinator<THub>>();

            services.AddHostedService<KinnaConnectionLeaseSweeper<THub>>();

            // 关掉时检查自己会短路成 Healthy，不必在注册期判 EnableHealthCheck
            services.AddHealthChecks()
                .AddCheck<KinnaConnectionHealthCheck<THub>>($"kinna:{typeof(THub).Name}");

            return services;
        }

        /// <summary>
        /// 组装单个 Hub 的配置：共享配置（AddVivConfig 注册的实例）→ 按 Hub 覆盖 → 代码回调。
        /// </summary>
        private static KinnaOptions BuildOptions(
            IServiceProvider provider,
            string hubName,
            Action<KinnaOptions>? configure)
        {
            // DI 里那份是全进程共享的单例，这里必须先拷一份再绑定：
            // 就地 Bind 会把上一个 Hub 的 HubPath / Prefix 覆盖掉，两个 Hub 还会共用同一个对象。
            var shared = provider.GetService<KinnaOptions>();
            var options = shared?.Clone() ?? new KinnaOptions();

            provider.GetService<IConfiguration>()?
                .GetSection($"VivOptions:KinnaOption:{hubName}")
                .Bind(options);

            configure?.Invoke(options);
            return options;
        }

        /// <summary>注册事件的推送器，同时把对应的投递器按 Hub 和事件闭合。</summary>
        public static IServiceCollection AddKinnaPusher<THub, TEvent, TPusher>(this IServiceCollection services)
            where THub : Hub
            where TEvent : class
            where TPusher : class, IKinnaPusher<TEvent>
        {
            services.AddScoped<IKinnaPusher<TEvent>, TPusher>();
            services.TryAddScoped<IKinnaMessageDispatcher<THub, TEvent>, KinnaMessageDispatcher<THub, TEvent>>();
            return services;
        }

        /// <summary>按本 Hub 的 HubPath 映射。</summary>
        public static WebApplication MapKinnaHub<THub>(this WebApplication app, string? pattern = null)
            where THub : Hub
        {
            var session = app.Services.GetRequiredService<IKinnaSession<THub>>();
            app.MapHub<THub>(pattern ?? session.Options.HubPath);
            return app;
        }
    }
}
