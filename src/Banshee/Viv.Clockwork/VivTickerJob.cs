using Viv.Contracts.Interface;
using Viv.Contracts.Models;

namespace Viv.Clockwork
{
    /// <summary>
    /// TickerQ 任务的公共入口：只保证"开工前有上下文快照"。
    ///
    /// 曾经这里还包了 <c>IVivLocalEventScope</c>（成功 Flush / 失败 Discard 本地事件），
    /// 但那让每个定时任务都被迫依赖一个多数任务用不到的端口。现在本地事件由需要的任务
    /// 自己注入作用域处理；跨进程投递走发件箱，写库走工作单元。
    /// </summary>
    public static class VivTickerJob
    {
        public static Task RunAsync(IVivContext context, Func<Task> work, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(work);

            EnsureSnapshot(context);
            return work();
        }

        public static Task<TResult> RunAsync<TResult>(IVivContext context, Func<Task<TResult>> work, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(work);

            EnsureSnapshot(context);
            return work();
        }

        /// <summary>
        /// 任务体跑之前必须有明确快照：租户（SubjectId &gt; 0）或系统租户（<see cref="VivContextContent.ForSystemJob"/>）。
        /// 没 SetSnapshot 的默认全 0 会让 EF 租户过滤失效，所以这里硬失败而不是静默带空上下文跑。
        /// </summary>
        public static void EnsureSnapshot(IVivContext context)
        {
            var snapshot = context.GetRawSnapshot();
            if (snapshot == null || snapshot.AppId <= 0)
            {
                throw new InvalidOperationException(
                    "Ticker 任务开始前必须先 SetSnapshot（租户或系统租户）。" +
                    "系统任务请调用 IVivContext.SetSnapshot(VivContextContent.ForSystemJob(appId))。");
            }
        }
    }
}
