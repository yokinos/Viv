using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using Viv.Log.VivLogger;
using Viv.Nana.Models;
using Viv.Nana.Options;
using Viv.Nana.RabbitMQ;
using Viv.Vva;
using Viv.Vva.Extension;

namespace Viv.Nana.Core
{
    public class NanaFactory
    {
        protected static readonly NanaOptions NanaOptions = VivConfigRegistry.Get<NanaOptions>() ?? new NanaOptions();
        protected static readonly ConcurrentDictionary<string, QueueModel> _queue_dict = [];
        protected static readonly ConcurrentDictionary<string, string> _queue_Re_dict = [];
        protected readonly IVivLogger _logger;

        public NanaFactory(IVivLogger logger)
        {
            _logger = logger;
        }

        [return: MaybeNull]
        protected QueueModel GetQueue<T>(T t) where T : VivMessage
        {
            var queueName = t.GetQueueName();

            if (_queue_dict.TryGetValue(queueName, out var queue))
            {
                return queue;
            }

            queue = t.GetQueue();
            if (queue != null)
            {
                _queue_dict.TryAdd(queueName, queue);
            }

            return queue;
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
            var model = GetQueue(message.Content);
            if (model is null || message.Content is null) return false;

            var channel = await VivRabbitClient.GetInstance().GetChannelAsync(model, cancellationToken);
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
    }
}
