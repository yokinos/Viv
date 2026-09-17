using System.Collections.Generic;
using System.Threading;
using Viv.Log;

namespace Viv.Engine.UnitOfWork
{
    /// <summary>
    /// 工作单元的启动期结论暂存处。
    ///
    /// 【为什么要有这个静态类】
    /// Autofac 注册发生在 <c>VivLocator.Initialize()</c> 之前，那会儿拿不到 <see cref="ILoggerContract"/>，
    /// 扫描结论无处可写。所以先存静态，等 <c>UnitOfWorkManager</c> 首次构造时补一条启动日志。
    /// 与 Nana 的 <c>NanaRegister.RecordLocalQueueScan</c> + <c>NanaLocalEventPublisher</c> 同一套做法。
    ///
    /// 【为什么要打这条日志】
    /// 拦截失效是<b>完全静默</b>的 —— 没代理上就没有事务，业务照跑、数据照写，只是不原子。
    /// 上线后极难排查，所以启动时必须留下一句「拦截了几处」。
    /// </summary>
    internal static class UnitOfWorkDiagnostics
    {
        private static int _typeCount;
        private static int _methodCount;
        private static List<string> _notCovered = [];
        private static int _logged;

        /// <summary>注册期记下扫描结论（本方法在 Autofac 配置阶段调用）</summary>
        public static void RecordScan(int typeCount, int methodCount, List<string> notCovered)
        {
            _typeCount = typeCount;
            _methodCount = methodCount;
            _notCovered = notCovered;
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

            // 类级特性承诺的是「整个类都是事务的」，但接口代理只拦得住可重写的异步方法。
            // 剩下的逐个列出来（含原因）—— 静默漏掉 = 业务以为在事务里、实际裸奔，
            // 是最难查的一类问题。
            foreach (var item in _notCovered)
            {
                logger.Warning("类级 [VivUnitOfWork] 未覆盖该公开方法，它不会开启事务：{0}", item);
            }
        }

        /// <summary>仅测试用：重置一次性的静态状态</summary>
        internal static void ResetForTest()
        {
            _typeCount = 0;
            _methodCount = 0;
            _notCovered = [];
            _logged = 0;
        }
    }
}
