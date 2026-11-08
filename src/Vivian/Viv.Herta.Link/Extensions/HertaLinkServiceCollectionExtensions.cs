using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Viv.EventContracts.Herta;
using Viv.Herta.Link.Adapters;
using Viv.Herta.Link.Dispatch;
using Viv.Herta.Link.Hubs;
using Viv.Kinna.Extensions;
using Viv.Kinna.Ports;
using Viv.Echo.Proxies;

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
            // Hub 是泛型，必须按具体类型闭合
            services.AddKinna<ChatHub>();

            // 原生 WebSocket 与 SignalR 共用同一套身份解析和准入：
            // 换传输不能换判定，否则小程序那条路就成了绕过 Apex 应用校验的口子
            services.AddSingleton<IKinnaIdentityResolver<ChatHub>, HertaIdentityResolver<ChatHub>>();

            // AddKinna 里已经 TryAdd 了只校验身份完整性的默认实现，这里必须 Replace 而不是 Add：
            // 精确类型注册优先于开放泛型，直接 Add 会让默认实现继续生效
            services.Replace(ServiceDescriptor.Scoped<IKinnaAdmission<ChatHub>, HertaKinnaAdmission<ChatHub>>());

            // 推送计划和投递器要 Hub + 事件两个类型参数
            services.AddKinnaPusher<ChatHub, SendMessageEvent, ChatMessagePusher>();
            services.AddKinnaPusher<ChatHub, MessageRecalledEvent, ChatRecallPusher>();

            // 准入问 Apex，不维护本地白名单
            services.AddVivRpcProxies(configuration);

            return services;
        }
    }
}
