using Viv.Contracts.Enums;
using Viv.Contracts.Exceptions;
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

        public NanaEventPublisher(IVivContext context, IMessageBus bus, ILoggerContract logger)
        {
            _context = context;
            _bus = bus;
            _logger = logger;
        }

        public async ValueTask<bool> PublishAsync<T>(T content, CancellationToken cancellationToken = default) where T : NanaEvent
        {
            if (content is null) return false;
            cancellationToken.ThrowIfCancellationRequested();

            var message = new NanaEnvelope<T>
            {
                Content = content,
                Context = _context.GetRawSnapshot()?.Clone(),
            };

            try
            {
                await _bus.PublishAsync(message);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw WrapMqException($"Publish failed for {typeof(T).Name}", ex);
            }
        }

        public async ValueTask<bool> PublishDelayAsync<T>(TimeSpan delayTTL, T content, CancellationToken cancellationToken = default) where T : NanaEvent
        {
            if (content is null) return false;
            if (delayTTL < TimeSpan.Zero) return false;
            cancellationToken.ThrowIfCancellationRequested();

            var message = new NanaEnvelope<T>
            {
                Content = content,
                Context = _context.GetRawSnapshot()?.Clone(),
                DelaySecond = delayTTL.TotalSeconds
            };

            try
            {
                await _bus.ScheduleAsync(message, delayTTL);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw WrapMqException($"SchedulePublish failed for {typeof(T).Name}", ex);
            }
        }

        public async ValueTask<bool> PublishDelayAsync<T>(TimeSpan delayTTL, NanaEnvelope<T> envelope, CancellationToken cancellationToken = default) where T : NanaEvent
        {
            if (envelope?.Content is null) return false;
            if (delayTTL < TimeSpan.Zero) return false;
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _bus.ScheduleAsync(envelope, delayTTL);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw WrapMqException($"SchedulePublish failed for {typeof(T).Name}", ex);
            }
        }

        private VivConnectionException WrapMqException(string message, Exception ex)
        {
            _logger.Error(message, ex);
            return new VivConnectionException(VivConnType.RabbitMQ, message, ex);
        }
    }
}
