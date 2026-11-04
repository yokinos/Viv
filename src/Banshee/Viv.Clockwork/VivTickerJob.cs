using Viv.Contracts.Interface;
using Viv.Contracts.Models;

namespace Viv.Clockwork
{
    /// <summary>
    /// TickerQ 任务的入口。开工前必须已有上下文快照，没有就直接抛。
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
        /// 开工前必须有租户（SubjectId &gt; 0）或系统租户快照。没 SetSnapshot 时 AppId 是 0，
        /// EF 租户过滤会失效，所以这里直接抛。
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
