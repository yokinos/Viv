using TickerQ.Utilities.Base;
using TickerQ.Utilities.Interfaces;
using Viv.Contracts.Interface;
using Viv.Log;
using Viv.Nana;

namespace Viv.SakuMai.Api.Jobs
{
    /// <summary>
    /// 每天 3 点执行的定时任务。
    /// </summary>
    public class EveryDayDaily3AMJob : BaseJob
    {
        public EveryDayDaily3AMJob(
            ILoggerContract logger,
            IVivEventPublisher eventPublisher,
            IVivContext vivContext,
            IVivLocalEventScope localEvents)
            : base(logger, eventPublisher, vivContext, localEvents)
        {
        }

        [TickerFunction(nameof(EveryDayDaily3AMJob), "0 3 * * *")]
        public Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken)
        {
            UseSystemTenant(_appId);
            return RunAsync(() =>
            {
                _logger.Info("执行每日3点定时任务");
                return Task.CompletedTask;
            }, cancellationToken);
        }
    }
}
