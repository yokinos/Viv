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

namespace Viv.Kinna.Extensions
{
    /// <summary>
    /// 长连接层的接入点。业务只在两处出现具体 Hub：AddKinna&lt;THub&gt;() 和 MapKinnaHub&lt;THub&gt;()。
    ///
    /// 共享配置由 <c>AddViv</c> 绑进 DI（<c>VivOptions:KinnaOption</c>），这里只按 Hub 补会话等状态；
    /// 多 Hub 时用 <c>VivOptions:KinnaOption:{Hub 类型名}</c> 覆盖。
    ///
    /// 为什么不能像别的子系统一样在 <c>AddViv</c> 里一次注册完：<c>IHubContext&lt;THub&gt;</c> 必须按具体
    /// Hub 类型解析，<c>AddViv</c> 拿不到业务 Hub 的类型，只能在宿主里用泛型方法闭合。
    /// </summary>
    public static class KinnaServiceCollectionExtensions
    {
        /// <param name="services">宿主容器</param>
        /// <param name="configure">代码里覆盖，多 Hub 时各给各的 HubPath 和 Prefix</param>
        public static IServiceCollection AddKinna<THub>(
            this IServiceCollection services,
            Action<KinnaOptions>? configure = null)
            where THub : Hub
        {
            // 幂等；EnableDetailedErrors 是进程级选项，由 AddViv 按 KinnaOption 配一次
            services.AddSignalR();

            services.TryAddSingleton<IKinnaSession<THub>>(sp =>
            {
                var options = BuildOptions(sp, typeof(THub).Name, configure);

                return new KinnaSession<THub>(
                    new KinnaConnectionPool(typeof(THub).Name),
                    new KinnaClientSender<THub>(sp.GetRequiredService<IHubContext<THub>>()),
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
            var options = provider.GetService<KinnaOptions>() ?? new KinnaOptions();

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
