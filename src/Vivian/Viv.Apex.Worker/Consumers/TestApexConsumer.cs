using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Interface;
using Viv.EventContracts.Apex;
using Viv.Log;
using Viv.Nana;

namespace Viv.Apex.Worker.Consumers
{
    public class TestApexConsumer : VivConsumer<TestApexEvent>
    {
        public TestApexConsumer(ILoggerContract logger, IVivContext context) : base(logger, context)
        {

        }

        public async override Task<SubscribeResult> ReceiveMessageAsync(NanaEnvelope<TestApexEvent> envelope, CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            return SubscribeResult.Success();
        }
    }
}
