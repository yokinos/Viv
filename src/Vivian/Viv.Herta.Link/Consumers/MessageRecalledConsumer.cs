using Viv.EventContracts.Herta;
using Viv.Herta.Link.Hubs;
using Viv.Kinna.Dispatch;
using Viv.Nana;

namespace Viv.Herta.Link.Consumers
{
    /// <summary>
    /// 撤回通知推送，拓扑与消息一致：每个副本各推自己的连接。
    /// </summary>
    [NanaConsumer(Topology = ConsumerTopology.PerInstance)]
    public class MessageRecalledConsumer : VivConsumer<MessageRecalledEvent>
    {
        private readonly IKinnaMessageDispatcher<ChatHub, MessageRecalledEvent> _dispatcher;

        public MessageRecalledConsumer(VivConsumerDependency dependency, IKinnaMessageDispatcher<ChatHub, MessageRecalledEvent> dispatcher)
            : base(dependency)
        {
            _dispatcher = dispatcher;
        }

        protected override object? GetConsumeLockKey(NanaEnvelope<MessageRecalledEvent> envelope) => null;

        public override async Task<SubscribeResult> ReceiveMessageAsync(
            NanaEnvelope<MessageRecalledEvent> envelope,
            CancellationToken cancellationToken = default)
        {
            var evt = envelope.Content;
            if (evt == null)
                return SubscribeResult.Failed(false, "Message content is null");

            var context = envelope.Context is { } content
                ? KinnaPushContext.From(content, envelope.MessageId)
                : null;

            var outcome = await _dispatcher.DispatchAsync(evt, context, cancellationToken).ConfigureAwait(false);

            return outcome is KinnaPushOutcome.Success or KinnaPushOutcome.Skipped
                ? SubscribeResult.Success()
                : SubscribeResult.Failed(false, "推送失败");
        }
    }
}
