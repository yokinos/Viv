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
        /// 设置上下文 用户事件跨进程传播基础数据
        /// </summary>
        /// <param name="appId"></param>
        /// <param name="subjectId"></param>
        public void SetContext(long appId, long subjectId)
        {
            _context.SetSnapshot(new Contracts.Models.VivContextContent()
            {
                AppId = appId,
                TraceId = IdMagic.NextId().ToString(),
                UserId = 999,
                SubjectId = subjectId
            });
        }
    }
}
