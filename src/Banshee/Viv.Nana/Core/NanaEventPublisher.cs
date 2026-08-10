using Viv.Contracts.Interface;
using Viv.Log;
using Wolverine;

namespace Viv.Nana.Core
{
    public class NanaEventPublisher : IVivEventPublisher
    {
        private readonly IVivContext _context;
        private readonly IMessageBus _bus;
        private readonly ILoggerContract _logger;

        public NanaEventPublisher(
            IVivContext context,
            IMessageBus bus,
            ILoggerContract logger)
        {
            _context = context;
            _bus = bus;
            _logger = logger;
        }

        public async Task<bool> PublishAsync<T>(T content, CancellationToken cancellationToken = default) where T : NanaEvent
        {
            if (content is null) return false;

            var message = new NanaEnvelope<T>
            {
                Content = content,
                Context = _context.GetRawSnapshot()?.Clone()
            };

            try
            {
                await _bus.PublishAsync(message);
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
                Content = content,
                Context = _context.GetRawSnapshot()?.Clone()
            };

            try
            {
                await _bus.ScheduleAsync(message, delayTTL);
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
