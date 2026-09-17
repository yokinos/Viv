using Microsoft.Extensions.DependencyInjection;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Options;

namespace Viv.Engine
{
    /// <summary>
    /// 启动时按实体同步表结构，由 <see cref="DatabaseOptions.SyncTableOnStartup"/> 门控（默认关）。
    /// 在 VivLocator.Initialize 之后、开始接请求之前调用。
    /// </summary>
    internal static class VivStartupSchemaSync
    {
        /// <summary>失败只记日志，不阻塞启动。</summary>
        public static void Run(IServiceProvider services)
        {
            // 未配 DatabaseOption 时这里是 null，等同开关关闭
            var options = services.GetService<DatabaseOptions>();
            if (options is not { SyncTableOnStartup: true }) return;

            // 单例，从根解析；放 try 外，作用域释放失败时也要能记上
            var logger = services.GetService<ILoggerContract>();

            try
            {
                // using 落在 try 内：作用域释放会 dispose IMomoDbContext，那一步同样可能抛
                using var scope = services.CreateScope();

                // 启动路径是同步的（RunVivApi / RunVivWorker 都返回 void），阻塞等一次
                scope.ServiceProvider.GetRequiredService<IMomoDbContext>()
                    .SyncTableAsync().GetAwaiter().GetResult();

                logger?.Info("启动表结构同步完成");
            }
            catch (Exception ex)
            {
                logger?.Error("启动表结构同步失败，已跳过", ex);
            }
        }
    }
}
