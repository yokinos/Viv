using Viv.EventContracts.Herta;
using Viv.Herta.Link.Dispatch;
using Viv.Nana;

namespace Viv.Herta.Link.Consumers
{
    /// <summary>
    /// 聊天消息推送。每个副本都收到全量消息（<see cref="ConsumerTopology.PerInstance"/>），
    /// 只推自己连接表里有的连接，单聊跨副本不漏推靠的就是这一点。
    ///
    /// 构造依赖必须注册在 IServiceCollection 里：Wolverine 内联构造 handler，解析不到的依赖会让消息进死信。
    /// </summary>
    [NanaConsumer(Topology = ConsumerTopology.PerInstance)]
    public class SendMessageConsumer : VivConsumer<SendMessageEvent>
    {
        private readonly IMessageDispatcher _dispatcher;

        public SendMessageConsumer(VivConsumerDependency dependency, IMessageDispatcher dispatcher)
            : base(dependency)
        {
            _dispatcher = dispatcher;
        }

        protected override object? GetConsumeLockKey(NanaEnvelope<SendMessageEvent> envelope) => null;

        /// <inheritdoc />
        public override Task<SubscribeResult> ReceiveMessageAsync(NanaEnvelope<SendMessageEvent> envelope, CancellationToken cancellationToken = default)
        {
            var evt = envelope.Content;
            if (evt == null)
                return Task.FromResult(SubscribeResult.Failed(false, "Message content is null"));

            return _dispatcher.DispatchAsync(envelope, evt, cancellationToken);
        }
    }
}
