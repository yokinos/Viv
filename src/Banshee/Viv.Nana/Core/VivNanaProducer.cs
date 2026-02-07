using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Interface;
using Viv.Nana.Enums;
using Viv.Nana.Models;

namespace Viv.Nana.Core
{
    public class VivNanaProducer : NanaFactory, IVivNanaProducer
    {
        private readonly IVivContext _context;

        public VivNanaProducer(IVivContext context)
        {
            _context = context;
        }

        public async Task<bool> PublishAsync<T>(T content) where T : NanaMessage
        {
            if (content is null) return false;

            var message = new VivMessage<T>()
            {
                VivAppId = _context.VivAppId,
                TenantId = _context.TenantId,
                Content = content
            };

            var flag = NanaOptions.MainQueueType switch
            {
                MessageQueueType.RabbitMQ => false,
                MessageQueueType.RedisPubSub => false,
                MessageQueueType.LocalMessage => false,
                _ => false
            };

        }

        public Task<bool> PublishDelayAsync<T>(T content) where T : NanaMessage
        {
            throw new NotImplementedException();
        }
    }
}
