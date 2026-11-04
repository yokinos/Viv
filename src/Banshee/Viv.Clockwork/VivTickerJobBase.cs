using Viv.Contracts.Interface;
using Viv.Contracts.Models;

namespace Viv.Clockwork
{
    /// <summary>
    /// Ticker 任务基类。子类的 [TickerFunction] 入口走 <see cref="RunAsync"/>，
    /// 它会先确认有租户或系统租户快照。
    ///
    /// 本地事件不在这里包。任务要发进程内事件，自己注入 IVivLocalEventScope。
    /// </summary>
    public class VivTickerJobBase
    {
        protected readonly IVivContext Context;

        protected VivTickerJobBase(IVivContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>执行任务体，先校验快照</summary>
        protected Task RunAsync(Func<Task> work, CancellationToken cancellationToken = default)
            => VivTickerJob.RunAsync(Context, work, cancellationToken);

        /// <summary>RunAsync 的带返回值版本</summary>
        /// </summary>
        protected Task<TResult> RunAsync<TResult>(Func<Task<TResult>> work, CancellationToken cancellationToken = default)
            => VivTickerJob.RunAsync(Context, work, cancellationToken);

        protected void SetContext(long subjectId, long appId, long userId = 0)
        {
            Context.SetSnapshot(new VivContextContent
            {
                AppId = appId,
                SubjectId = subjectId,
                UserId = userId,
                TraceId = Guid.NewGuid().ToString("N")
            });
        }

        protected void UseSystemTenant(long appId)
            => Context.SetSnapshot(VivContextContent.ForSystemJob(appId));
    }
}
