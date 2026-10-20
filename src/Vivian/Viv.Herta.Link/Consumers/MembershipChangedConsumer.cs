using Viv.EventContracts.Herta;
using Viv.Herta.Link.Hubs;
using Viv.Nana;

namespace Viv.Herta.Link.Consumers
{
    [NanaConsumer(Topology = ConsumerTopology.PerInstance)]
    public class MembershipChangedConsumer : VivConsumer<MembershipChangedEvent>
    {
        private readonly IMembershipSync _sync;

        public MembershipChangedConsumer(VivConsumerDependency dependency, IMembershipSync sync)
            : base(dependency)
        {
            _sync = sync;
        }

        protected override object? GetConsumeLockKey(NanaEnvelope<MembershipChangedEvent> envelope) => null;

        public override async Task<SubscribeResult> ReceiveMessageAsync(
            NanaEnvelope<MembershipChangedEvent> envelope,
            CancellationToken cancellationToken = default)
        {
            var evt = envelope.Content;
            if (evt == null)
                return SubscribeResult.Failed(false, "Message content is null");

            await _sync.ApplyAsync(evt, cancellationToken).ConfigureAwait(false);
            return SubscribeResult.Success();
        }
    }
}
