using Microsoft.Extensions.DependencyInjection;
using Viv.Delusion.Magic;
using Viv.Outbox.Core;
using Viv.Outbox.Options;

namespace Viv.Outbox
{
    /// <summary>
    /// 发件箱装配，由 <c>VivRegister.RegisterOutbox</c> 调用。
    /// </summary>
    public static class OutboxRegister
    {
        /// <summary>
        /// 配置就绪时注册发件箱，并在 <c>EnableDispatcher</c> 打开时挂上投递器的后台宿主。
        ///
        /// 这里会注册 <c>IHostedService</c>，而框架其余部分从不自己注册宿主服务。
        /// 原因是会写发件箱的服务不止 Apex / DeepRed 这两个有 Worker 的，Herta.Api / SakuMai.Api 也会写；
        /// 投递器只在 Worker 跑的话这些服务的消息就永远发不出去，而让每个宿主手工 AddHostedService
        /// 又违背「一行启动」。
        /// </summary>
        public static void Initialize(IServiceCollection services, OutboxOptions options)
        {
            // 业务 Core 常是懒加载程序集，不先强制加载的话事件类型索引是残缺的，
            // 表现成「库里的消息投递不出去」
            TypeScanMagic.ForceLoadReferencedAssemblies();

            // 无状态（发布器逐次传入），进程内一份就够
            services.AddSingleton<IOutboxEnvelopeSenderFactory, OutboxEnvelopeSenderFactory>();

            // Scoped —— 与业务拿到的 IMomoDbContext 同作用域，入队才并入业务事务
            services.AddScoped<IOutboxRepository, OutboxRepository>();
            services.AddScoped<IVivOutbox, OutboxStore>();

            if (options.EnableDispatcher)
            {
                services.AddHostedService<OutboxDispatcher>();
            }
        }
    }
}
