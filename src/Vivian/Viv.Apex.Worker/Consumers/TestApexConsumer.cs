using Viv.EventContracts.Apex;
using Viv.Nana;

namespace Viv.Apex.Worker.Consumers
{
    public class TestApexConsumer : VivConsumer<TestApexEvent>
    {
        public TestApexConsumer(VivConsumerDependency dependency) : base(dependency)
        {
        }

        public override Task<SubscribeResult> ReceiveMessageAsync(NanaEnvelope<TestApexEvent> envelope, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SubscribeResult.Success());
        }
    }
}
