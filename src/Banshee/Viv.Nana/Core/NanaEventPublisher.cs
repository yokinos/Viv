using MassTransit;
using Viv.Contracts.Interface;
using Viv.Emt;
using Viv.Nana.Models;

namespace Viv.Nana.Core
{
    public class NanaEventPublisher : IVivPublisher
    {
        private readonly IVivContext _context;
        private readonly IPublishEndpoint _publishEndpoint;
        private readonly IMessageScheduler _scheduler;
        private readonly IEmtLogger _logger;

        public NanaEventPublisher(
            IVivContext context,
            IPublishEndpoint publishEndpoint,
            IMessageScheduler scheduler,
            IEmtLogger logger)
        {
            _context = context;
            _publishEndpoint = publishEndpoint;
            _scheduler = scheduler;
            _logger = logger;
        }

        public async Task<bool> PublishAsync<T>(T content) where T : NanaEvent
        {
            if (content is null) return false;

            var message = new NanaMessage<T>
            {
                AppId = _context.AppId,
                TenantId = _context.TenantId,
                Content = content
            };

            try
            {
                await _publishEndpoint.Publish(message);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Publish failed for {typeof(T).Name}", ex);
                return false;
            }
        }

        public async Task<bool> PublishDelayAsync<T>(TimeSpan delayTTL, T content) where T : NanaEvent
        {
            if (content is null) return false;
            if (delayTTL < TimeSpan.Zero) return false;

            var message = new NanaMessage<T>
            {
                AppId = _context.AppId,
                TenantId = _context.TenantId,
                Content = content
            };

            try
            {
                await _scheduler.SchedulePublish(delayTTL, message);
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
