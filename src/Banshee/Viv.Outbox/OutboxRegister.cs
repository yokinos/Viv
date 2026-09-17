using Microsoft.Extensions.DependencyInjection;
using Viv.Delusion.Magic;
using Viv.Outbox.Core;
using Viv.Outbox.Options;

namespace Viv.Outbox
{
    /// <summary>
    /// 发件箱装配。由 <c>VivRegister.RegisterOutbox</c> 调用 ——
    /// 配置驱动、宿主零手工接线（与 <c>AddVivGrpcKestrel</c> 同一姿态）。
    /// </summary>
    public static class OutboxRegister
    {
        /// <summary>
        /// <b>会注册 <c>IHostedService</c></b> —— 这是本模块对既有约定的一处刻意破坏，说清楚原因：
        ///
        /// <para>
        /// 框架此前从不自己注册宿主服务。但写发件箱的服务不止 Apex / DeepRed 这两个有 Worker 的，
        /// Herta.Api / SakuMai.Api 也会写；投递器只在 Worker 跑的话，这些服务的消息就永远发不出去。
        /// 让每个宿主手工 <c>AddHostedService</c> 又违背「一行启动」的框架姿态。
        /// </para>
        ///
        /// <para>
        /// <c>OutboxOptions</c> / <c>IOptions&lt;OutboxOptions&gt;</c> 由 <c>VivConfigLoader.AddVivConfig</c>
        /// 按 <c>VivOptions.OutboxOption</c> 节点统一注册，这里不重复注册。
        /// </para>
        /// </summary>
        public static void Initialize(IServiceCollection services, OutboxOptions options)
        {
            // 业务 Core 常是懒加载程序集。不先强制加载，事件类型索引就是残缺的 ——
            // 表现成「库里的消息投递不出去」，很难查。
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
