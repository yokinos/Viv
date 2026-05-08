using System;
using Viv.Contracts.Interface;
using Viv.Log;
using Viv.Nana.Enums;
using Viv.Nana.LocalMessage;
using Viv.Nana.Models;
using Viv.Redis;

namespace Viv.Nana.Core
{
    public class NanaProducer : NanaFactory, IVivProducer
    {
        private readonly IVivContext _context;
        private readonly Lazy<ILocalMessageRespository> _localMessageRespository;

        public NanaProducer(IVivContext context, IDistributedLogger logger, Lazy<IRedisService> redisService, Lazy<ILocalMessageRespository> localMessageRespository)
            : base(logger, redisService)
        {
            _context = context;
            _localMessageRespository = localMessageRespository;
        }

        public async Task<bool> PublishAsync<T>(T content) where T : VivMessage
        {
            if (content is null) return false;
            var message = new NanaMessage<T>()
            {
                AppId = _context.AppId,
                TenantId = _context.TenantId,
                Content = content
            };

            var flag = await VivPublishAsync(NanaOptions.MainQueueType, message);
            if (!flag)
            {
                flag = await VivPublishAsync(NanaOptions.SecondaryQueueType, message);
                if (!flag && NanaOptions.IsEnableLocalMessage)
                {
                    flag = await VivPublishAsync(MessageQueueType.LocalMessage, message);
                }
            }

            return flag;
        }

        public async Task<bool> PublishDelayAsync<T>(TimeSpan delayTTL, T content) where T : VivMessage
        {
            if (content is null) return false;
            if (!content.IsDelayQueue) return false;

            if (delayTTL < TimeSpan.Zero)
            {
                return false;
            }

            content.DelayTTL = delayTTL;
            return await PublishAsync<T>(content);
        }

        private async Task<bool> VivPublishAsync<T>(MessageQueueType queueType, NanaMessage<T> message) where T : VivMessage
        {
            return queueType switch
            {
                MessageQueueType.RabbitMQ => await RabbitMQPublishAsync(message, NanaOptions.RetryCount),
                MessageQueueType.RedisPubSub => await RedisPublishAsync(message),
                MessageQueueType.LocalMessage => await AddLocalMessage(message),
                MessageQueueType.None => false,
                _ => false,
            };
        }

        public async Task<bool> AddLocalMessage<T>(NanaMessage<T> message) where T : VivMessage
        {
            if (_localMessageRespository is null || _localMessageRespository.Value is null)
            {
                return false;
            }

            return await _localMessageRespository.Value.AddMessageAsync(message);
        }
    }
}
