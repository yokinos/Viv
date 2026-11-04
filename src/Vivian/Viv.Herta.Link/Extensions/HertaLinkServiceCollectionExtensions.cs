using Microsoft.Extensions.DependencyInjection.Extensions;
using Viv.EventContracts.Herta;
using Viv.Herta.Link.Adapters;
using Viv.Herta.Link.Dispatch;
using Viv.Herta.Link.Hubs;
using Viv.Kinna.Extensions;
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

            // 推送计划和投递器要 Hub + 事件两个类型参数
            services.AddKinnaPusher<ChatHub, SendMessageEvent, ChatMessagePusher>();
            services.AddKinnaPusher<ChatHub, MessageRecalledEvent, ChatRecallPusher>();

            // 准入问 Apex，不维护本地白名单
            services.AddVivRpcProxies(configuration);

            return services;
        }
    }
}
