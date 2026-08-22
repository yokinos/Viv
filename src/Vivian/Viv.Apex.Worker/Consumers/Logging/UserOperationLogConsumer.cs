using System;
using System.Collections.Generic;
using System.Text;
using Viv.EventContracts.Apex.Logging;
using Viv.Nana;

namespace Viv.Apex.Worker.Consumers.Logging
{
    public class UserOperationLogConsumer : VivConsumer<UserOperationLogEvent>
    {
        public UserOperationLogConsumer(VivConsumerDependency dependency) : base(dependency)
        {

        }

        public override async Task<SubscribeResult> ReceiveMessageAsync(NanaEnvelope<UserOperationLogEvent> envelope, CancellationToken cancellationToken = default)
        {


            return SubscribeResult.Success();
        }
    }
}
