using Microsoft.Extensions.DependencyInjection.Extensions;
using Viv.EventContracts.Herta;
using Viv.Herta.Link.Adapters;
using Viv.Herta.Link.Dispatch;
using Viv.Herta.Link.Hubs;
using Viv.Kinna.Extensions;
using Viv.Kinna.Ports;
using Viv.ServiceProxy.Apex;

namespace Viv.Herta.Link.Extensions
{
    public static class HertaLinkServiceCollectionExtensions
    {
        /// <summary>
        /// 实时层接线。通用部分由 AddKinna 负责，这里补 Herta 的两个推送计划、成员同步、
        /// 两个端口适配器，以及按 Apex 数据校验 AppId 的准入。不装 Redis 背板。
        /// </summary>
        public static IServiceCollection AddHertaLink(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddKinna<ChatHub>(configuration);

            // 每个事件一行，注册推送计划的同时把投递器闭合
            services.AddKinnaPusher<ChatHub, SendMessageEvent, ChatMessagePusher>();
            services.AddKinnaPusher<ChatHub, MessageRecalledEvent, ChatRecallPusher>();

            services.TryAddScoped<IMembershipSync, MembershipSync>();

            // 连接准入靠 Apex 的客户端应用数据，不维护本地白名单
            services.AddApexClientAppQuery(configuration);
            services.TryAddScoped<HertaLinkAdmission>();

            // 两个端口要给实现，Kinna 不提供默认值：群列表拿不到时报错，比返回空列表好查
            services.AddScoped<IKinnaGroupSource<ChatHub>, HertaGroupSource>();
            services.AddScoped<IKinnaPresenceSink<ChatHub>, HertaPresenceSink>();

            // 投递确认落 Redis（每用户一条水位），给 Resync 判断要不要补拉
            services.AddScoped<IKinnaDeliveryStore<ChatHub>, HertaDeliveryStore>();

            return services;
        }
    }
}
