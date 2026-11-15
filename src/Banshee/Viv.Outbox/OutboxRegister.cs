using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Viv.Contracts.Interface;
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
        /// 投递器只在 Worker 跑的话这些服务的消息就永远发不出去，而让每个宿主手工 AddHostedService
        /// 又违背「一行启动」。
        /// </summary>
        public static void Initialize(IServiceCollection services, OutboxOptions options)
        {
            // 业务 Core 常是懒加载程序集，不先强制加载的话事件类型索引是残缺的，
            // 表现成「库里的消息投递不出去」
            TypeScanMagic.ForceLoadReferencedAssemblies();

            // 认领时刻、租约、退避、保留期都从这里取。TryAdd 而不是 Add：
            // Kinna、其它模块也会注册，重复 Add 会变成多个实例，虽然行为一样但没必要。
            services.TryAddSingleton(TimeProvider.System);

            // 唤醒信号：进程内一份。所有业务作用域往同一条队列里投，投递器从同一条队列里取；
            // 注册成多个实例的话，写方叫醒的就不是在读的那个投递器了，快路径静默失效。
            services.TryAddSingleton<OutboxSignal>();

            // Scoped —— 与 IMomoDbContext / OutboxStore 同作用域，「入队」与「这一次提交」才是一笔账。
            // ITransactionObserver 单独注册一份指向同一个实例：Momo 按 IEnumerable<ITransactionObserver>
            // 解析，不这样注册它就拿不到。
            services.AddScoped<OutboxCommitObserver>();
            services.AddScoped<ITransactionObserver>(sp => sp.GetRequiredService<OutboxCommitObserver>());

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
