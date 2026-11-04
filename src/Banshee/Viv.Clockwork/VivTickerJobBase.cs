using Viv.Contracts.Interface;
using Viv.Contracts.Models;

namespace Viv.Clockwork
{
    /// <summary>
    /// Viv Ticker 任务基类。子类的 [TickerFunction] 入口走 <see cref="RunAsync"/>，
    /// 只做一件事：开工前确保有上下文快照（租户或系统租户）。
    ///
    /// 本地事件不在这里包装 —— 定时任务默认只写库 / 发 MQ，这两条路的事务语义分别由
    /// 工作单元和发件箱负责。哪个任务确实要发进程内事件，自己注入 <c>IVivLocalEventScope</c>
    /// 在自己的回调里包一层，不要强加给所有任务。
    /// </summary>
    public class VivTickerJobBase
    {
        protected readonly IVivContext Context;

        protected VivTickerJobBase(IVivContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>
        /// 执行任务体：先校验快照，没有直接失败，不带着空上下文跑
        /// </summary>
        protected Task RunAsync(Func<Task> work, CancellationToken cancellationToken = default)
            => VivTickerJob.RunAsync(Context, work, cancellationToken);

        /// <summary>
        /// <see cref="RunAsync(Func{Task}, CancellationToken)"/> 的带返回值版本
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
