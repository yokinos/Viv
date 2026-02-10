using System;
using System.Collections.Generic;
using System.Text;
using Viv.Autofac;
using Viv.Contracts.Interface;
using Viv.Log.VivLogger;
using Viv.Nana.Enums;
using Viv.Nana.Interface;
using Viv.Nana.Models;

namespace Viv.Nana.Core
{
    public class NanaProducer : NanaFactory, IVivProducer
    {
        private readonly IVivContext _context;

        public NanaProducer(IVivContext context, IVivLogger logger) : base(logger)
        {
            _context = context;
        }

        public async Task<bool> PublishAsync<T>(T content) where T : VivMessage
        {
            if (content is null) return false;
            var message = new NanaMessage<T>()
            {
                VivAppId = _context.VivAppId,
                TenantId = _context.TenantId,
                Content = content
            };

            var flag = await VivPublishAsync(NanaOptions.MainQueueType, message);
            if (!flag)
            {
                flag = await VivPublishAsync(NanaOptions.SecondaryQueueType, message);
                if (!flag && NanaOptions.IsEnableLocalMessage)
                {
                    var respository = VivLocator.GetScopedService<ILocalMessageRespository>();
                    if (respository is not null)
                    {
                        flag = await respository.AddMessageAsync(message);
                    }
                }
            }

            return flag;
        }

        private async Task<bool> VivPublishAsync<T>(MessageQueueType queueType, NanaMessage<T> message) where T : VivMessage
        {
            return queueType switch
            {
                MessageQueueType.RabbitMQ => await RabbitMQPublishAsync(message, NanaOptions.RetryCount),
                MessageQueueType.RedisPubSub => false,
                MessageQueueType.LocalMessage => false,
                MessageQueueType.None => false,
                _ => false
            };
        }

        public Task<bool> PublishDelayAsync<T>(T content) where T : VivMessage
        {
            throw new NotImplementedException();
        }
    }
}
