using TickerQ.Utilities.Base;
using TickerQ.Utilities.Interfaces;
using Viv.Contracts.Interface;
using Viv.Delusion;
using Viv.EventContracts.Apex;
using Viv.Log;
using Viv.Nana;
using Viv.Redis;

namespace Viv.SakuMai.Api.Jobs
{
    public class Interval30SecondJob : BaseJob
    {
        public Interval30SecondJob(ILoggerContract logger, IVivEventPublisher vivPublisher, IVivContext vivContext)
            : base(logger, vivPublisher, vivContext)
        {

        }

        [TickerFunction(nameof(Interval30SecondJob), "*/30 * * * * *")]
        public async Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken = default)
        {
            await _eventPublisher.PublishDelayAsync(TimeSpan.FromSeconds(15),new TestApexEvent()
            {
                IsJob = true,
                TestTime = DateTime.UtcNow,
            }, cancellationToken);
        }
    }
}
