using Viv.Clockwork;
using Viv.Contracts.Interface;
using Viv.Log;
using Viv.Nana;

namespace Viv.SakuMai.Api.Jobs
{
    public class BaseJob : VivTickerJobBase
    {
        protected readonly ILoggerContract _logger;
        protected readonly IVivEventPublisher _eventPublisher;

        public BaseJob(
            ILoggerContract logger,
            IVivEventPublisher eventPublisher,
            IVivContext vivContext,
            IVivLocalEventScope localEvents)
            : base(localEvents, vivContext)
        {
            _logger = logger;
            _eventPublisher = eventPublisher;
        }

        /// <summary>
        /// Job 进程的 AppId。在 Viv 的设计中，即使是定时任务也会认定为一个客户端。
        /// </summary>
        protected readonly long _appId = 235814647;

        /// <summary>
        /// 设置租户上下文，供事件跨进程传播。
        /// </summary>
        public void SetContext(long subjectId, long? appId = null)
            => SetTenant(subjectId, appId ?? _appId, userId: 999);
    }
}
