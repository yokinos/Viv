using MassTransit;
using Viv.Emt;
using Viv.Nana.Core;
using Viv.Nana.Models;

namespace Viv.Nana
{
    public abstract class VivConsumer<T> : IConsumer<NanaMessage<T>> where T : NanaEvent
    {
        protected readonly IEmtLogger _logger;

        protected VivConsumer(IEmtLogger logger)
        {
            _logger = logger;
        }

        public abstract Task<SubscribeResult> ReceiveMessageAsync(NanaMessage<T> message, CancellationToken cancellationToken = default);

        public async Task Consume(ConsumeContext<NanaMessage<T>> context)
        {
            if (context == null || context.Message == null || context.Message.Content == null)
            {
                // 没啥记录的必要 直接丢弃就完事了
                return;
            }

            var result = await ReceiveMessageAsync(context.Message, context.CancellationToken);

            if (result.IsSuccess)
                return;

            if (result.IsRequeue)
            {
                throw new NanaConsumeException(result.Message);
            }

            _logger.Error($"Message consumption failed (not requeued): {result.Message}, MessageId: {context.Message.MessageId}");
        }
    }
}
