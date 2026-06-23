using TickerQ.Utilities.Base;
using TickerQ.Utilities.Interfaces;
using Viv.Delusion;
using Viv.EventContracts.Apex;
using Viv.Log;
using Viv.Nana;

namespace Viv.SakuMai.Api.Jobs
{
    public class Interval30SecondJob
    {
        private readonly ILoggerContract _logger;
        private readonly IVivPublisher _vivPublisher;

        public Interval30SecondJob(ILoggerContract logger, IVivPublisher vivPublisher)
        {
            _logger = logger;
            _vivPublisher = vivPublisher;
        }

        [TickerFunction(nameof(Interval30SecondJob), "*/30 * * * * *")]
        public async Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken = default)
        {
            await _vivPublisher.PublishAsync(new TestApexEvent()
            {
                IsJob = true,
                TestTime = DateTime.UtcNow,
            }, cancellationToken);
        }
    }
}
