using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Viv.Herta.Link.Adapters;
using Viv.Herta.Link.Hubs;
using Viv.Kinna.Extensions;
using Viv.Kinna.Ports;
using Viv.Keke.Proxies;

namespace Viv.Herta.Link.Extensions
{
    public static class HertaLinkServiceCollectionExtensions
    {
        /// <summary>
        /// 实时层接线。成员同步、准入、端口适配器都靠 IDependency 自动注册，
        /// 这里只剩按 Hub / 事件闭合的泛型注册和代理注册。
        /// </summary>
        public static IServiceCollection AddHertaLink(this IServiceCollection services, IConfiguration configuration)
        {
            // 一个 Hub 一行。新加 Hub 只加这里，别手动抄那几行 —— 漏掉 Replace 就会退回
            // AddKinna 里的默认准入（只校验身份完整性），等于给新入口开了后门
            services.AddHertaKinnaHub<ChatHub>();
            services.AddHertaKinnaHub<LineHub>();

            // 准入问 Apex，不维护本地白名单
            services.AddVivRpcProxies(configuration);

            return services;
        }

        /// <summary>
        /// 把一个 Hub 的 Kinna 运行时整个装上：会话/连接表/发送器、身份解析、准入、投递器。
        ///
        /// 身份和准入必须是 Herta 的实现而不是框架默认值 —— 原生 WebSocket 与 SignalR 共用
        /// 同一张连接表，换传输不能换判定，否则小程序那条路就成了绕过 Apex 应用校验的口子。
        /// </summary>
        private static IServiceCollection AddHertaKinnaHub<THub>(this IServiceCollection services) where THub : Hub
        {
            services.AddKinna<THub>();
            services.TryAddSingleton<IKinnaIdentityResolver<THub>, HertaIdentityResolver<THub>>();
            services.Replace(ServiceDescriptor.Scoped<IKinnaAdmission<THub>, HertaKinnaAdmission<THub>>());
            services.AddKinnaPusher<THub>();
            return services;
        }
    }
}
