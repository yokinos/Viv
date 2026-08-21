using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Interface;
using Viv.EventContracts.Apex;
using Viv.EventContracts.Apex.Logging;
using Viv.Log;
using Viv.Nana;

namespace Viv.Apex.Worker.Consumers.Logging
{
    public class UserOperationLogConsumer : VivConsumer<UserOperationLogEvent>
    {
        public UserOperationLogConsumer(ILoggerContract logger, IVivContext context) : base(logger, context)
        {

        }

        public override async Task<SubscribeResult> ReceiveMessageAsync(NanaEnvelope<UserOperationLogEvent> envelope, CancellationToken cancellationToken = default)
        {


            return SubscribeResult.Success();
        }
    }
}
