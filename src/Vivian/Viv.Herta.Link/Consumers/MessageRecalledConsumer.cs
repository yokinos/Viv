using Viv.EventContracts.Herta;
using Viv.Herta.Link.Dispatch;
using Viv.Nana;

namespace Viv.Herta.Link.Consumers
{
    [NanaConsumer(Topology = ConsumerTopology.PerInstance)]
    public class MessageRecalledConsumer : VivConsumer<MessageRecalledEvent>
    {
        private readonly IMessageDispatcher _dispatcher;

        public MessageRecalledConsumer(VivConsumerDependency dependency, IMessageDispatcher dispatcher)
            : base(dependency)
        {
            _dispatcher = dispatcher;
        }

        protected override object? GetConsumeLockKey(NanaEnvelope<MessageRecalledEvent> envelope) => null;

        public override Task<SubscribeResult> ReceiveMessageAsync(
            NanaEnvelope<MessageRecalledEvent> envelope,
            CancellationToken cancellationToken = default)
        {
            var evt = envelope.Content;
            if (evt == null)
                return Task.FromResult(SubscribeResult.Failed(false, "Message content is null"));

            return _dispatcher.DispatchRecallAsync(envelope, evt, cancellationToken);
        }
    }
}
