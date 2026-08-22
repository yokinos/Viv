using Viv.Contracts.Interface;
using Viv.Delusion.Magic;
using Viv.Log;
using Viv.Nana;

namespace Viv.SakuMai.Api.Jobs
{
    public class BaseJob
    {
        protected readonly ILoggerContract _logger;
        protected readonly IVivEventPublisher _eventPublisher;
        protected readonly IVivContext _context;

        public BaseJob(ILoggerContract logger, IVivEventPublisher eventPublisher, IVivContext vivContext)
        {
            _logger = logger;
            _eventPublisher = eventPublisher;
            _context = vivContext;
        }

        /// <summary>
        /// Job进程的AppId 在Viv的设计中 即使是定时任务也会认定为一个客户端
        /// </summary>
        protected readonly long _appId = 235814647;

        /// <summary>
        /// 设置上下文 用户事件跨进程传播基础数据
        /// </summary>
        /// <param name="appId"></param>
        /// <param name="subjectId"></param>
        public void SetContext(long subjectId, long? appId = null)
        {
            _context.SetSnapshot(new Contracts.Models.VivContextContent()
            {
                AppId = appId ?? _appId,
                TraceId = IdMagic.NextId().ToString(),
                UserId = 999,
                SubjectId = subjectId
            });
        }
    }
}
