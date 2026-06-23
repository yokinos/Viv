using System;
using System.Collections.Generic;
using System.Text;
using Viv.EventContracts.Apex;
using Viv.Log;
using Viv.Nana;

namespace Viv.Apex.Worker.Consemer
{
    public class TestApexConsumer : VivConsumer<TestApexEvent>
    {
        public TestApexConsumer(ILoggerContract logger) : base(logger)
        {

        }

        public async override Task<SubscribeResult> ReceiveMessageAsync(NanaEnvelope<TestApexEvent> message, CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            return SubscribeResult.Success();
        }
    }
}
