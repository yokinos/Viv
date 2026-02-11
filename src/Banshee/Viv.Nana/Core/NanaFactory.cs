using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using Viv.Log.VivLogger;
using Viv.Nana.Models;
using Viv.Nana.Options;
using Viv.Nana.RabbitMq;
using Viv.Redis;
using Viv.Vva;
using Viv.Vva.Extension;

namespace Viv.Nana.Core
{
    public class NanaFactory
    {
        protected static readonly NanaOptions NanaOptions = VivConfigRegistry.Get<NanaOptions>() ?? new NanaOptions();
        protected static readonly ConcurrentDictionary<string, QueueModel> _queue_dict = [];
        protected static readonly ConcurrentDictionary<string, string> _queue_Re_dict = [];

        protected readonly Lazy<IRedisService> _redisService;
        protected readonly IVivLogger _logger;

        public NanaFactory(IVivLogger logger, Lazy<IRedisService> redisService)
        {
            _logger = logger;
            _redisService = redisService;
        }

        #region RabbitMQ发布消息

        /// <summary>
        /// 发布消息到RabbitMQ
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="message"></param>
        /// <param name="retryCount"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async ValueTask<bool> RabbitMQPublishAsync<T>(NanaMessage<T> message, int retryCount, CancellationToken cancellationToken = default) where T : VivMessage
        {
            if (message is null || message.Content is null) return false;
            var model = message.Content.GetQueue();
            if (model is null || message.Content is null) return false;

            var channel = await VivRabbitClient.GetInstance().GetChannelAsync(model, message.Content.GetDeadLetterQueue(), cancellationToken);
            if (channel is null || channel.IsClosed) return false;

            var properties = message.Content.GetBasicProperties(message.VivAppId, message.MessageId);
            if (properties is null)
            {
                return false;
            }

            //  限制重试次数在合理范围内，防止过度重试导致资源浪费
            int maxRetry = retryCount < 1 || retryCount > 10 ? 3 : retryCount;
            int currentRetry = 0;

            var isPublished = false;
            while (currentRetry < maxRetry && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await DeclareChannelNack(channel, message);
                    await channel.BasicPublishAsync(model.Exchange, model.RoutingKey, model.IsMandatory, properties, message.ToBytes(), cancellationToken);
                    isPublished = true;
                    break;
                }
                catch (OperationInterruptedException ex)
                {
                    _logger.Error($"第{currentRetry + 1}次发布失败：RabbitMQ操作中断", ex);
                }
                catch (BrokerUnreachableException ex)
                {
                    _logger.Error($"第{currentRetry + 1}次发布失败：无法连接到RabbitMQ服务器", ex);
                }
                catch (Exception ex)
                {
                    _logger.Error($"第{currentRetry + 1}次发布失败：{ex.Message},[{message.ToJson()}]", ex);
                }

                // 如果还没成功且还有重试次数，等待后重试（指数退避）
                if (!isPublished && currentRetry < maxRetry && !cancellationToken.IsCancellationRequested)
                {
                    // 指数退避
                    int delayMs = (int)Math.Pow(2, currentRetry) * 1000;
                    await Task.Delay(delayMs, cancellationToken);
                }
            }

            return isPublished;
        }

        private static async ValueTask DeclareChannelNack<T>(IChannel channel, NanaMessage<T> message) where T : VivMessage
        {
            return;

            channel.BasicAcksAsync += async (sender, e) =>
            {

            };

            channel.BasicNacksAsync += async (sender, e) =>
            {

            };

            channel.BasicReturnAsync += async (sender, e) =>
            {
                var errorMsg = $"AMQP退回消息,交换机为Exchange为[{e.Exchange}],路由键Routingkey为[{e.RoutingKey}],状态码RelayCode为[{e.ReplyCode}],退回原因RelayText=[{e.ReplyText}]";
            };
        }

        #endregion

        #region Redis发布消息

        public async ValueTask<bool> RedisPublishAsync<T>(NanaMessage<T> message) where T : VivMessage
        {
            if (message is null || message.Content is null) return false;
            var queue = message.Content.GetQueueName();
            var clientCount = await _redisService.Value.PublishAsync(RedisChannel.Pattern(queue), message);
            return clientCount > 0;
        }

        #endregion
    }
}
