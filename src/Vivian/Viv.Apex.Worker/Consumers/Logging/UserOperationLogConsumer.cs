using System;
using System.Collections.Generic;
using System.Text;
using Viv.EventContracts.Apex;
using Viv.EventContracts.Apex.Logging;
using Viv.Log;
using Viv.Nana;

namespace Viv.Apex.Worker.Consumers.Logging
{
    public class UserOperationLogConsumer : VivConsumer<UserOperationLogEvent>
    {
        public UserOperationLogConsumer(ILoggerContract logger) : base(logger)
        {

        }

        public override Task<SubscribeResult> ReceiveMessageAsync(NanaEnvelope<UserOperationLogEvent> envelope, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}
