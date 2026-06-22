using TickerQ.Utilities.Base;
using TickerQ.Utilities.Interfaces;
using Viv.Delusion;
using Viv.Log;
using Viv.Nana;
using Viv.Tick;

namespace Viv.SakuMai.Api.Jobs
{
    /// <summary>
    /// 每天3点执行的定时任务
    /// </summary>
    public class EveryDayDaily3AMJob
    {
        private readonly ILoggerContract _logger;
        private readonly IVivPublisher _vivPublisher;

        public EveryDayDaily3AMJob(ILoggerContract logger, IVivPublisher vivPublisher)
        {
            _logger = logger;
            _vivPublisher = vivPublisher;
        }

        [TickerFunction(nameof(EveryDayDaily3AMJob), "0 3 * * *")]
        public async Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken)
        {
            _logger.Info("执行每日3点定时任务");
        }
    }
}
