using Microsoft.Extensions.DependencyInjection;

using Viv.Delusion.Magic;
using Viv.Skirk.Core;
using Viv.Skirk.Options;

namespace Viv.Skirk
{
    /// <summary>
    /// 异步任务装配，由 <c>VivRegister</c> 在 <c>VivOptions.SkirkOption</c> 非空时调用。
    /// </summary>
    public static class VivAsyncTaskRegister
    {
        /// <summary>
        /// 注册端口与实现。
        ///
        /// <paramref name="option"/>.<c>EnableWorker</c> 决定本宿主跑不跑消费者：
        /// Api 与 Worker 共享同一个库，谁来执行只看这一项，两个都开也安全（认领是数据库原子的）。
        /// </summary>
        public static void Initialize(IServiceCollection services, SkirkOption option)
        {
            services.AddSingleton(option);
            services.AddSingleton<VivAsyncTaskHandlerRegistry>();

            // Scoped：与业务同一个 IMomoDbContext 作用域，入队才能并入业务事务
            services.AddScoped<VivAsyncTaskStore>();
            services.AddScoped<IVivAsyncTask, VivAsyncTaskService>();

            if (!option.EnableWorker) return;

            // 业务 Core 常是懒加载程序集，不先加载的话 handler 扫不到
            TypeScanMagic.ForceLoadReferencedAssemblies();

            foreach (var handlerType in TypeScanMagic.ScanByInterface<IVivAsyncTaskHandler>())
            {
                services.AddScoped(handlerType);
            }

            services.AddHostedService<VivAsyncTaskWorker>();
        }
    }
}
