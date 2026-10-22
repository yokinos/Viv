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
    /// 每次调用给一个 Hub 建一套会话，所以一个进程可以装多个 Hub，互不影响。
    /// 配置先读共享的 KinnaOptions，再读 KinnaOptions:&lt;Hub 类型名&gt; 覆盖，最后执行 configure。
    /// </summary>
    public static class KinnaServiceCollectionExtensions
    {
        /// <param name="services">宿主容器</param>
        /// <param name="configuration">配置源</param>
        /// <param name="configure">代码里覆盖，多 Hub 时各给各的 HubPath 和 Prefix</param>
        public static IServiceCollection AddKinna<THub>(
            this IServiceCollection services,
            IConfiguration configuration,
            Action<KinnaOptions>? configure = null)
            where THub : Hub
        {
            var options = new KinnaOptions();
            configuration.GetSection(nameof(KinnaOptions)).Bind(options);
            configuration.GetSection($"{nameof(KinnaOptions)}:{typeof(THub).Name}").Bind(options);
            configure?.Invoke(options);

            services.TryAddSingleton<IKinnaSession<THub>>(sp => new KinnaSession<THub>(
                new KinnaConnectionPool(typeof(THub).Name),
                new KinnaClientSender<THub>(sp.GetRequiredService<IHubContext<THub>>()),
                new KinnaKeys(options.Prefix),
                options));

            services.TryAddSingleton<KinnaSweepClock<THub>>();
            services.TryAddSingleton<KinnaUplinkLimiter<THub>>();

            services.TryAddScoped<IKinnaConnectionCoordinator<THub>, KinnaConnectionCoordinator<THub>>();

            services.AddHostedService<KinnaConnectionLeaseSweeper<THub>>();

            if (options.EnableHealthCheck)
            {
                // 一个 Hub 一条，多 Hub 时 /health 里能分别看到各自的连接数和清扫状态
                services.AddHealthChecks()
                    .AddCheck<KinnaConnectionHealthCheck<THub>>($"kinna:{typeof(THub).Name}");
            }

            // EnableDetailedErrors 是 SignalR 的进程级选项，多个 Hub 时以最后一次注册为准
            services.AddSignalR(o => o.EnableDetailedErrors = options.EnableDetailedErrors);

            return services;
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
