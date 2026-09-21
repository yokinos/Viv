using TickerQ.Utilities.Base;
using TickerQ.Utilities.Interfaces;
using Viv.Contracts.Interface;
using Viv.EventContracts.Apex;
using Viv.Log;
using Viv.Nana;

namespace Viv.SakuMai.Api.Jobs
{
    public class Interval30SecondJob : BaseJob
    {
        public Interval30SecondJob(
            ILoggerContract logger,
            IVivEventPublisher vivPublisher,
            IVivContext vivContext,
            IVivLocalEventScope localEvents)
            : base(logger, vivPublisher, vivContext, localEvents)
        {
        }

        [TickerFunction(nameof(Interval30SecondJob), "*/30 * * * * *")]
        public Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken = default)
        {
            SetContext(11, 222);
            return RunAsync(async () =>
            {
                await _eventPublisher.PublishDelayAsync(TimeSpan.FromSeconds(15), new TestApexEvent()
                {
                    IsJob = true,
                    TestTime = DateTime.UtcNow,
                }, cancellationToken);
            }, cancellationToken);
        }
    }
}
