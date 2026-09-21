using Microsoft.Extensions.DependencyInjection;
using Viv.Contracts.Interface;
using Viv.Outbox.Core;
using Viv.Outbox.Options;

namespace Viv.Outbox
{
    /// <summary>
    /// 可选 Inbox。配了数据库就注册，不强迫消费者使用。
    ///
    /// 写入端是可选的，清理端不是 —— 表既然由框架写，就由框架保证它不会无限涨。
    /// </summary>
    public static class InboxRegister
    {
        /// <param name="options">
        /// 允许为 null：Inbox 的启用条件是「配了数据库」，不要求 InboxOption 节点存在，
        /// 缺席时用默认值（保留 7 天）。要关掉清理把保留期配成 0 或负数。
        /// </param>
        public static void Initialize(IServiceCollection services, InboxOptions? options = null)
        {
            services.AddScoped<IInboxRepository, InboxRepository>();
            services.AddScoped<IVivInbox, InboxStore>();

            services.AddSingleton(options ?? new InboxOptions());
            services.AddHostedService<InboxDispatcher>();
        }
    }
}
