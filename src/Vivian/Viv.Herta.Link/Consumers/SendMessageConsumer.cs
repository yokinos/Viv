using Viv.EventContracts.Herta;
using Viv.Herta.Link.Hubs;
using Viv.Kinna.Dispatch;
using Viv.Nana;

namespace Viv.Herta.Link.Consumers
{
    /// <summary>
    /// 聊天消息推送。每个副本都收到全量消息（<see cref="ConsumerTopology.PerInstance"/>），
    /// 只推自己连接表里有的连接。
    ///
    /// 依赖必须能从容器解析：Wolverine 内联构造 handler，解析不到消息就进死信。
    /// </summary>
    [NanaConsumer(Topology = ConsumerTopology.PerInstance)]
    public class SendMessageConsumer : VivConsumer<SendMessageEvent>
    {
        private readonly IKinnaMessageDispatcher<ChatHub, SendMessageEvent> _dispatcher;

        public SendMessageConsumer(VivConsumerDependency dependency, IKinnaMessageDispatcher<ChatHub, SendMessageEvent> dispatcher)
            : base(dependency)
        {
            _dispatcher = dispatcher;
        }

        protected override object? GetConsumeLockKey(NanaEnvelope<SendMessageEvent> envelope) => null;

        /// <inheritdoc />
        public override async Task<SubscribeResult> ReceiveMessageAsync(NanaEnvelope<SendMessageEvent> envelope, CancellationToken cancellationToken = default)
        {
            var evt = envelope.Content;
            if (evt == null)
                return SubscribeResult.Failed(false, "Message content is null");

            var context = envelope.Context is { } content
                ? KinnaPushContext.From(content, envelope.MessageId)
                : null;

            var outcome = await _dispatcher.DispatchAsync(evt, context, cancellationToken).ConfigureAwait(false);

            // Skipped 是目标不在本机；失败也不回队，缺口由客户端补拉
            return outcome is KinnaPushOutcome.Success or KinnaPushOutcome.Skipped
                ? SubscribeResult.Success()
                : SubscribeResult.Failed(false, "推送失败");
        }
    }
}
