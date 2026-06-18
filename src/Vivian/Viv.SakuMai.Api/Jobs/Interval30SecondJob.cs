using TickerQ.Utilities.Base;
using Viv.Delusion;
using Viv.Log;
using Viv.Nana;
using Viv.Tick.TickerQCore;

namespace Viv.SakuMai.Api.Jobs
{
    public class Interval30SecondJob : ITickerQTask
    {
        private readonly ILoggerContract _logger;
        private readonly IVivPublisher _vivPublisher;

        public Interval30SecondJob(ILoggerContract logger, IVivPublisher vivPublisher)
        {
            _logger = logger;
            _vivPublisher = vivPublisher;
        }

        [TickerFunction(nameof(Interval30SecondJob), "*/30 * * * * *")]
        public async Task<FuncResult> ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken = default)
        {
            _logger.Info("Executing Interval30SecondJob...");

            return FuncResult.Success();
        }
    }
}
