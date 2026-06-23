using MassTransit;
using Viv.Contracts.Interface;
using Viv.Log;

namespace Viv.Nana.Core
{
    public class NanaEventPublisher : IVivPublisher
    {
        private readonly IVivContext _context;
        private readonly IPublishEndpoint _publishEndpoint;
        private readonly IMessageScheduler _scheduler;
        private readonly ILoggerContract _logger;

        public NanaEventPublisher(
            IVivContext context,
            IPublishEndpoint publishEndpoint,
            IMessageScheduler scheduler,
            ILoggerContract logger)
        {
            _context = context;
            _publishEndpoint = publishEndpoint;
            _scheduler = scheduler;
            _logger = logger;
        }

        public async Task<bool> PublishAsync<T>(T content, CancellationToken cancellationToken = default) where T : NanaEvent
        {
            if (content is null) return false;

            var message = new NanaEnvelope<T>
            {
                AppId = _context.AppId,
                TenantId = _context.TenantId,
                Content = content
            };

            try
            {
                await _publishEndpoint.Publish(message, cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Publish failed for {typeof(T).Name}", ex);
                return false;
            }
        }

        public async Task<bool> PublishDelayAsync<T>(TimeSpan delayTTL, T content, CancellationToken cancellationToken = default) where T : NanaEvent
        {
            if (content is null) return false;
            if (delayTTL < TimeSpan.Zero) return false;

            var message = new NanaEnvelope<T>
            {
                AppId = _context.AppId,
                TenantId = _context.TenantId,
                Content = content
            };

            try
            {
                await _scheduler.SchedulePublish(delayTTL, message, cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"SchedulePublish failed for {typeof(T).Name}", ex);
                return false;
            }
        }
    }
}
