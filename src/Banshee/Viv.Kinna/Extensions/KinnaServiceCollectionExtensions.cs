using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Viv.Kinna.Connections;
using Viv.Kinna.Contracts;
using Viv.Kinna.Dispatch;
using Viv.Kinna.Health;
using Viv.Kinna.Hubs;
using Viv.Kinna.Options;
using Viv.Kinna.Ports;
using Viv.Kinna.WebSockets;
using Viv.Log;

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
            // 幂等；EnableDetailedErrors 是进程级选项，由 AddViv 按 KinnaOption 配一次。
            // 只跑原生 WS 时这行仍然要留：配置要到运行时绑定才拿得到 EnableSignalR，
            // 注册期没法跳过。它只注册服务、不开端点，关掉开关后 IHubContext 不会被解析
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
            // 只是没人往里登记，发送器分流时全部落到 SignalR 那一侧（关掉 SignalR 时直接丢弃）
            services.TryAddSingleton<KinnaWebSocketRegistry<THub>>(sp =>
            {
                var options = sp.GetRequiredService<KinnaHubSetup<THub>>().Options;

                return new KinnaWebSocketRegistry<THub>(
                    options.WebSocketQueueCapacity,
                    new KinnaWebSocketTimeouts(
                        options.WebSocketCloseTimeout,
                        options.WebSocketTerminateGrace,
                        options.WebSocketShutdownGrace),
                    sp.GetService<TimeProvider>());
            });

            services.TryAddSingleton<IKinnaSession<THub>>(sp =>
            {
                var options = sp.GetRequiredService<KinnaHubSetup<THub>>().Options;

                // 关掉 SignalR 时不解析 IHubContext：既省掉一个用不到的依赖，
                // 也让「只跑原生 WS」的宿主不必为了 Hub 类型能被解析而额外准备什么
                IKinnaClientSender signalr = options.EnableSignalR
                    ? new KinnaClientSender<THub>(sp.GetRequiredService<IHubContext<THub>>())
                    : NoopKinnaClientSender.Instance;

                return new KinnaSession<THub>(
                    new KinnaConnectionPool(typeof(THub).Name, sp.GetService<TimeProvider>()),
                    new CompositeKinnaClientSender<THub>(
                        sp.GetRequiredService<KinnaWebSocketRegistry<THub>>(),
                        signalr,
                        options.EnableSignalR),
                    new KinnaKeys(options.Prefix),
                    options);
            });

            services.TryAddSingleton<KinnaSweepClock<THub>>();
            services.TryAddSingleton<KinnaUplinkLimiter<THub>>();

            services.TryAddScoped<IKinnaConnectionCoordinator<THub>, KinnaConnectionCoordinator<THub>>();

            // 注册闸门必须是单例：协调器本身是 scoped，靠它自己串行化等于每次请求一把新锁
            services.TryAddSingleton<KinnaRegistrationGate<THub>>();

            services.AddHostedService<KinnaConnectionLeaseSweeper<THub>>();

            // 跨副本踢人的订阅端。没注册 IKinnaKickBus 时整体空转，单副本部署不受影响
            services.AddHostedService<KinnaKickSubscriber<THub>>();

            // 关掉时检查自己会短路成 Healthy，不必在注册期判 EnableHealthCheck。
            //
            // AddCheck 走的是 Configure 追加而不是 TryAdd，同一 Hub 重复 AddKinna 会多注册一条
            // 同名检查（每次 /health 多跑一遍）。这里用标记服务挡一道，和 AddHostedService 的
            // 去重语义对齐
            if (!services.Any(d => d.ServiceType == typeof(KinnaHealthCheckRegistered<THub>)))
            {
                services.AddSingleton<KinnaHealthCheckRegistered<THub>>();
                services.AddHealthChecks()
                    .AddCheck<KinnaConnectionHealthCheck<THub>>($"kinna:{typeof(THub).Name}");
            }

            return services;
        }

        /// <summary>「这个 Hub 的健康检查已经注册过」的标记，只用于去重。</summary>
        private sealed class KinnaHealthCheckRegistered<THub>
        {
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

            // 两条传输都关掉时这个 Hub 整体空转：连接表永远为空，清扫器、健康检查、
            // 踢人订阅全在白跑，而业务方只会看到「推送发出去了但没人收到」。
            // 注册期拿不到最终配置（共享配置要到运行时才绑定），这里是最早能同时
            // 看到两个开关的地方；不抛异常，宿主可能就是先关掉再慢慢接
            if (!options.EnableSignalR && string.IsNullOrWhiteSpace(options.WebSocketPath))
            {
                provider.GetService<ILoggerContract>()?.Warning(
                    $"[kinna:{hubName}] EnableSignalR=false 且 WebSocketPath 为空："
                    + "这个 Hub 没有任何传输可接入，所有下行推送都会落空");
            }

            return options;
        }

        /// <summary>
        /// 扫一遍就把本 Hub 的推送器全装上。Pusher 类只要声明
        /// <see cref="IKinnaPusher{THub, TEvent}"/>，注册处不必再把 Hub 和事件抄一遍。
        ///
        /// 默认扫 <typeparamref name="THub"/> 所在程序集，Pusher 放在别的程序集时把那个程序集传进来。
        /// 重复调用是幂等的；同一个 (Hub, 事件) 出现两个 Pusher 会在注册期直接报错 ——
        /// 「后注册的盖掉先注册的」是上线之后查不出来的那种错。
        /// </summary>
        /// <param name="services">宿主容器</param>
        /// <param name="extraAssemblies">除 Hub 所在程序集之外还要扫的程序集</param>
        public static IServiceCollection AddKinnaPusher<THub>(
            this IServiceCollection services,
            params Assembly[] extraAssemblies)
            where THub : Hub
        {
            var hub = typeof(THub);

            // 先收齐再登记：同一次扫描里出现两个 Pusher 抢同一个事件时，
            // 要么先发现问题报错，要么一条都别注册，不能注册一半
            var claimed = new Dictionary<Type, Type>();

            foreach (var pusher in KinnaPusherScanner.Find<THub>(extraAssemblies))
            {
                foreach (var contract in pusher.GetInterfaces())
                {
                    if (!contract.IsGenericType || contract.GetGenericTypeDefinition() != typeof(IKinnaPusher<,>))
                        continue;

                    var arguments = contract.GetGenericArguments();
                    if (arguments[0] != hub)
                        continue;

                    var eventType = arguments[1];
                    if (claimed.TryGetValue(eventType, out var owner) && owner != pusher)
                    {
                        throw new InvalidOperationException(
                            $"[kinna:{hub.Name}] 事件 {eventType.FullName} 有两个 Pusher："
                            + $"{owner.FullName} 和 {pusher.FullName}。一个 (Hub, 事件) 只能有一个 Pusher。");
                    }

                    claimed[eventType] = pusher;
                }
            }

            foreach (var pair in claimed)
            {
                RegisterPusher(services, hub, pair.Key, pair.Value);
            }

            return services;
        }

        /// <summary>
        /// 显式注册一个 Pusher。常规场景用扫描版即可，这个留给裁剪/AOT（反射扫类型会被裁掉）
        /// 或需要精确控制注册的时候。两条路径共用同一份登记逻辑，重复校验也一样。
        /// </summary>
        public static IServiceCollection AddKinnaPusher<THub, TEvent, TPusher>(this IServiceCollection services)
            where THub : Hub
            where TEvent : class
            where TPusher : class, IKinnaPusher<THub, TEvent>
        {
            RegisterPusher(services, typeof(THub), typeof(TEvent), typeof(TPusher));
            return services;
        }

        /// <summary>
        /// 一个 (Hub, 事件) 登记三件事：Pusher 具体类型、带 Hub 的契约、投递器，
        /// 外加把前两者钉成同一个实例。
        ///
        /// 契约那条用 ImplementationType 注册而不是工厂，是为了再调一次注册时能认出
        /// 「已经是这个实现」—— 认得出才能幂等，认不出就只能覆盖或者一律报错。
        /// </summary>
        private static void RegisterPusher(IServiceCollection services, Type hub, Type eventType, Type pusherType)
        {
            var contract = typeof(IKinnaPusher<,>).MakeGenericType(hub, eventType);

            var existing = services.FirstOrDefault(descriptor => descriptor.ServiceType == contract);
            if (existing != null)
            {
                if (existing.ImplementationType == pusherType)
                    return;

                throw new InvalidOperationException(
                    $"[kinna:{hub.Name}] 事件 {eventType.FullName} 已经注册过 Pusher"
                    + $"（{existing.ImplementationType?.FullName ?? "工厂注册"}），这里又来了 {pusherType.FullName}。"
                    + "一个 (Hub, 事件) 只能有一个 Pusher。");
            }

            services.AddScoped(contract, pusherType);

            // 业务注入具体类型和注入契约拿到同一个实例，不必为每次注入再构造一份
            services.AddScoped(pusherType, provider => provider.GetRequiredService(contract));

            services.TryAddScoped(
                typeof(IKinnaMessageDispatcher<,>).MakeGenericType(hub, eventType),
                typeof(KinnaMessageDispatcher<,>).MakeGenericType(hub, eventType));
        }

        /// <summary>
        /// 按本 Hub 的 HubPath 映射。
        ///
        /// <see cref="KinnaOptions.EnableSignalR"/> 为 false 时直接返回：这样即使宿主没删掉
        /// 这行调用，也不会把 <c>/hubs/*</c> 暴露出去。配置说话，不靠调用方记得清理。
        /// </summary>
        public static WebApplication MapKinnaHub<THub>(this WebApplication app, string? pattern = null)
            where THub : Hub
        {
            var session = app.Services.GetRequiredService<IKinnaSession<THub>>();

            if (!session.Options.EnableSignalR)
            {
                return app;
            }

            // 新加 Hub 时最容易漏的一步：不继承基类也能编译、也能握手成功，
            // 但连接不会进连接表 —— 推送找不到人、顶号踢不到、租约也扫不到。
            // 全程不报错，只是消息发不出去，排查成本很高，所以在启动时喊一声
            if (!typeof(KinnaHubBase<THub>).IsAssignableFrom(typeof(THub)))
            {
                app.Services.GetService<ILoggerContract>()?.Warning(
                    $"[kinna:{session.Hub}] {typeof(THub).FullName} 没有继承 KinnaHubBase<{typeof(THub).Name}>，"
                    + "它的连接不会被登记，下行推送和租约清扫都找不到它。");
            }

            app.MapHub<THub>(pattern ?? session.Options.HubPath);
            return app;
        }
    }
}
