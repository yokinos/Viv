using Viv.Contracts.Interface;
using Viv.Contracts.Models;

namespace Viv.Clockwork
{
    /// <summary>
    /// Viv Ticker 任务基类。子类的 [TickerFunction] 入口走 <see cref="RunAsync"/>，
    /// 默认包上 <see cref="IVivLocalEventScope"/>（成功 Flush / 失败 Discard）并校验租户快照。
    /// </summary>
    public abstract class VivTickerJobBase
    {
        protected readonly IVivLocalEventScope LocalEvents;
        protected readonly IVivContext Context;

        protected VivTickerJobBase(IVivLocalEventScope localEvents, IVivContext context)
        {
            LocalEvents = localEvents ?? throw new ArgumentNullException(nameof(localEvents));
            Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>包一层本地事件作用域再跑任务体。调用前请 <see cref="SetTenant"/> 或 <see cref="UseSystemTenant"/>。</summary>
        protected Task RunAsync(Func<Task> work, CancellationToken cancellationToken = default)
            => VivTickerJob.ExecuteAsync(LocalEvents, Context, work, cancellationToken);

        protected void SetTenant(long subjectId, long appId, long userId = 0)
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
