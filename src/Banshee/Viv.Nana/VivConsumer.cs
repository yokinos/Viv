using MassTransit;
using Viv.Log;
using Viv.Nana.Core;
using Viv.Nana.Models;

namespace Viv.Nana
{
    public abstract class VivConsumer<T> : IConsumer<NanaMessage<T>> where T : VivEvent, new()
    {
        protected readonly IDistributedLogger _logger;

        protected VivConsumer(IDistributedLogger logger)
        {
            _logger = logger;
        }

        public abstract Task<SubscribeResult> ReceiveMessageAsync(NanaMessage<T> message, CancellationToken cancellationToken = default);

        public async Task Consume(ConsumeContext<NanaMessage<T>> context)
        {
            var result = await ReceiveMessageAsync(context.Message, context.CancellationToken);

            if (result.IsSuccess)
                return;

            if (result.IsRequeue)
            {
                throw new VivMessageConsumeException(result.Message);
            }

            _logger.Error($"Message consumption failed (not requeued): {result.Message}, MessageId: {context.Message.MessageId}");
        }
    }
}
