using Microsoft.Extensions.DependencyInjection;
using Viv.Contracts.Interface;
using Viv.Outbox.Core;

namespace Viv.Outbox
{
    /// <summary>
    /// 可选 Inbox。配了数据库就注册，不强迫消费者使用。
    /// </summary>
    public static class InboxRegister
    {
        public static void Initialize(IServiceCollection services)
        {
            services.AddScoped<IInboxRepository, InboxRepository>();
            services.AddScoped<IVivInbox, InboxStore>();
        }
    }
}
