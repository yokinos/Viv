using TickerQ.Utilities.Base;
using TickerQ.Utilities.Interfaces;
using Viv.Delusion;
using Viv.EventContracts.Apex;
using Viv.Log;
using Viv.Nana;
using Viv.Redis;

namespace Viv.SakuMai.Api.Jobs
{
    public class Interval30SecondJob
    {
        private readonly ILoggerContract _logger;
        private readonly IVivEventPublisher _vivPublisher;
        private readonly IRedisService _redisService;

        public Interval30SecondJob(ILoggerContract logger, IVivEventPublisher vivPublisher, IRedisService redisService)
        {
            _logger = logger;
            _vivPublisher = vivPublisher;
            _redisService = redisService;
        }

        [TickerFunction(nameof(Interval30SecondJob), "*/30 * * * * *")]
        public async Task ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken = default)
        {
            await _redisService.AddAsync("1111", "xxxx");

            await _vivPublisher.PublishAsync(new TestApexEvent()
            {
                IsJob = true,
                TestTime = DateTime.UtcNow,
            }, cancellationToken);
        }
    }
}
