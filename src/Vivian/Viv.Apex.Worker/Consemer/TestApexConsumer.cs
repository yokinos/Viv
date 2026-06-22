using System;
using System.Collections.Generic;
using System.Text;
using Viv.EventContracts.Apex;
using Viv.Log;
using Viv.Nana;
using Viv.Nana.Models;

namespace Viv.Apex.Worker.Consemer
{
    public class TestApexConsumer : VivConsumer<TestApexEvent>
    {
        public TestApexConsumer(ILoggerContract logger) : base(logger)
        {

        }

        public async override Task<SubscribeResult> ReceiveMessageAsync(NanaMessage<TestApexEvent> message, CancellationToken cancellationToken = default)
        {
            _logger.Debug("Apex Consumer Execute Success");
            await Task.CompletedTask;
            return SubscribeResult.Success();
        }
    }
}
