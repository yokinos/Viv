using Viv.Contracts.Interface;
using Viv.Contracts.Models;

namespace Viv.Clockwork
{
    /// <summary>
    /// TickerQ 任务的默认包装：成功 Flush 本地事件、失败 Discard，开工前必须有租户/系统租户快照。
    /// SakuMai 的 <c>BaseJob</c> 走 <see cref="VivTickerJobBase"/>，所有 [TickerFunction] 入口都经这里。
    /// </summary>
    public static class VivTickerJob
    {
        public static Task ExecuteAsync(
            IVivLocalEventScope scope,
            IVivContext context,
            Func<Task> work,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(scope);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(work);
            EnsureSnapshot(context);
            return scope.RunAsync(work, cancellationToken);
        }

        public static Task<TResult> ExecuteAsync<TResult>(
            IVivLocalEventScope scope,
            IVivContext context,
            Func<Task<TResult>> work,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(scope);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(work);
            EnsureSnapshot(context);
            return scope.RunAsync(work, cancellationToken);
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
