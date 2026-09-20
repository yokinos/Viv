using System.Threading;
using Viv.Log;

namespace Viv.Engine.UnitOfWork
{
    /// <summary>
    /// 工作单元的启动期结论暂存处。
    ///
    /// Autofac 注册发生在 <c>VivLocator.Initialize()</c> 之前，那时拿不到 <see cref="ILoggerContract"/>，
    /// 扫描结论先存静态，等 <c>UnitOfWorkManager</c> 首次构造时补一条启动日志。
    /// 与 NanaRegister.RecordLocalQueueScan 同一套做法。
    ///
    /// 这条日志必须打：拦截失效是完全静默的，启动时至少留下一句「拦截了几处」。
    /// 类级特性拦不到的方法现在是启动期硬失败，不再用 Warning 放行。
    /// </summary>
    internal static class UnitOfWorkDiagnostics
    {
        private static int _typeCount;
        private static int _methodCount;
        private static int _logged;

        /// <summary>注册期记下扫描结论（本方法在 Autofac 配置阶段调用）</summary>
        public static void RecordScan(int typeCount, int methodCount)
        {
            _typeCount = typeCount;
            _methodCount = methodCount;
        }

        /// <summary>首次构造工作单元时打一次。</summary>
        public static void LogOnce(ILoggerContract logger)
        {
            if (Interlocked.Exchange(ref _logged, 1) != 0) return;

            var types = _typeCount;
            var methods = _methodCount;

            if (types == 0)
            {
                logger.Info("工作单元已就绪：当前没有方法标记 [VivUnitOfWork]");
            }
            else
            {
                logger.Info("工作单元已就绪：{0} 个类型、{1} 个方法开启方法级事务", types, methods);
            }
        }

        /// <summary>仅测试用：重置一次性的静态状态</summary>
        internal static void ResetForTest()
        {
            _typeCount = 0;
            _methodCount = 0;
            _logged = 0;
        }
    }
}
