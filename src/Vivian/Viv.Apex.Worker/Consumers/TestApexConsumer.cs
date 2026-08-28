using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Interface;
using Viv.EventContracts.Apex;
using Viv.Nana;

namespace Viv.Apex.Worker.Consumers
{
    public class TestApexConsumer : VivConsumer<TestApexEvent>
    {
        private readonly IDistributedLock _distributedLock;

        public TestApexConsumer(VivConsumerDependency dependency, IDistributedLock distributedLock) : base(dependency)
        {
            _distributedLock = distributedLock;
        }

        public async override Task<SubscribeResult> ReceiveMessageAsync(NanaEnvelope<TestApexEvent> envelope, CancellationToken cancellationToken = default)
        {
            var result = await _distributedLock.AcquireLockAsync(envelope.MessageId, TimeSpan.FromSeconds(15), async () =>
            {
                return SubscribeResult.Success();
            }, cancellationToken);

            return result;
        }
    }
}
