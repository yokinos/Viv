using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StackExchange.Redis;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Viv.Log;
using Viv.Nana.Core;
using Viv.Nana.Models;
using Viv.Nana.RabbitMq;
using Viv.Redis;
using Viv.Vva.Extension;

namespace Viv.Nana
{
    /// <summary>
    /// 适配Viv框架的消费者基类
    /// 特性：自动重试、降级处理
    /// 支持队列类型：
    /// 1. RabbitMQ（推送死信队列，支持消费死信，提供默认实现允许重写）
    /// 2. Redis发布订阅（无死信机制，失败后降级到本地消息表）
    /// 3. 本地消息表（最终兜底消费）
    /// </summary>
    /// <typeparam name="T">消息模型（需要继承[VivMessage]）</typeparam>
    public abstract class VivConsumer<T> : IVivConsumer, IDisposable where T : VivMessage, new()
    {
        protected readonly Lazy<IRedisService> _redisService;
        protected readonly IDistributedLogger _logger;

        private bool _disposed = false;
        private RedisChannel _redisChannel;
        private readonly HashSet<string> _queues = [];

        public VivConsumer(IDistributedLogger logger, Lazy<IRedisService> redisService)
        {
            _redisService = redisService;
            _logger = logger;
        }

        /// <summary>
        /// 接收消息
        /// </summary>
        /// <param name="message"></param>
        /// <param name="isDeadLetter"></param>
        /// <returns></returns>
        public abstract Task<SubscribeResult> ReceiveMessageAsync(NanaMessage<T> message, bool isDeadLetter = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// 重试次数
        /// 针对RabbitMQ，失败后重试次数，超过后进入死信队列；
        /// 针对Redis Pub/Sub，失败后重试次数，超过后降级到本地消息表；
        /// 针对本地消息表，失败后重试次数，超过后记录日志等待人工干预
        /// </summary>
        public int RetryCount { get; private set; } = 3;

        /// <summary>
        /// 订阅消息
        /// </summary>
        /// <returns></returns>
        public virtual async Task SubscribeAsync(CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.Warning("订阅操作已被取消，无需执行");
                return;
            }

            var vivMessage = new T();
            // 订阅RabbitMQ消息（先订阅死信队列，后订阅正常队列，确保死信优先处理）
            await RabbitMQSubscribeAsync(vivMessage, true, cancellationToken);
            await RabbitMQSubscribeAsync(vivMessage, false, cancellationToken);

            // 订阅Redis消息（Redis服务校验提前，避免无效调用）
            if (_redisService.IsValueCreated && _redisService.Value != null)
            {
                await RedisSubscribeAsync(vivMessage, cancellationToken);
            }
            else
            {
                _logger.Warning("Redis服务未初始化，跳过Redis消息订阅");
            }
        }

        private async Task<SubscribeResult> InvokeReceiveMessage(NanaMessage<T> message, int retryCount, bool isDeadLetter = false, CancellationToken cancellationToken = default)
        {
            for (int i = 0; i < retryCount; i++)
            {
                var subscribeResult = await ReceiveMessageAsync(message, isDeadLetter, cancellationToken);
                if (subscribeResult.IsSuccess)
                {
                    return subscribeResult;
                }
                else
                {
                    var delaySeconds = (int)Math.Pow(2, i);
                    await Task.Delay(delaySeconds, cancellationToken);
                }
            }

            return new SubscribeResult(false, false, $"消息处理失败，重试{retryCount}次后仍然失败");
        }

        public virtual async Task RabbitMQSubscribeAsync(T vivMessage, bool isDeadLetter, CancellationToken cancellationToken = default)
        {
            var queue = isDeadLetter ? vivMessage.GetDeadLetterQueue() : vivMessage.GetQueue();
            if (queue is null) return;

            var channel = await VivRabbitClient.GetInstance().GetChannelAsync(queue, null, cancellationToken);
            if (channel is null) return;

            _queues.Add(queue.QueueName);

            // 创建消费者并绑定事件
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (sender, args) =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    // 消息被取消：拒绝消息并重新入队，等待下一次消费
                    await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: true, cancellationToken);
                    _logger.Warning($"RabbitMQ消费被取消：队列[{queue.QueueName}]，DeliveryTag：[{args.DeliveryTag}]");
                    return;
                }

                try
                {
                    byte[] messageBody = args.Body.ToArray();
                    if (messageBody.IsNullOrEmpty())
                    {
                        // 直接[确认]丢弃消息
                        await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken);
                        return;
                    }
                    var messageJson = messageBody.ExtToString();
                    if (messageJson.IsNullOrEmpty())
                    {
                        // 直接[确认]丢弃消息
                        await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken);
                        return;
                    }
                    var nanaMessage = messageJson.As<NanaMessage<T>>();
                    if (nanaMessage == null)
                    {
                        // 直接[确认]丢弃消息
                        await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken);
                        _logger.Error($"RabbitMQ消息反序列化失败：队列[{queue.QueueName}]，DeliveryTag：{args.DeliveryTag}，消息内容：{messageJson}");
                        return;
                    }

                    // 处理消息
                    var subscribeResult = await InvokeReceiveMessage(nanaMessage, RetryCount, isDeadLetter, cancellationToken);
                    if (subscribeResult.IsSuccess)
                    {
                        // 消费成功：手动确认消息（关键：autoAck=false时必须确认）
                        await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken);
                    }
                    else
                    {
                        if (isDeadLetter)
                        {
                            // 死信消费失败：记录日志并[确认]丢弃消息，避免死信循环
                            await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken);

                            // 后续这里可以入库（先放着吧 我后面再改）
                        }
                        else
                        {
                            // 消费失败：拒绝消息并决定是否重新入队
                            // requeue=false：消息进入死信队列；requeue=true：重新入队等待重试
                            _logger.Error($"RabbitMQ消息消费失败，队列[{queue.QueueName}]，DeliveryTag：{args.DeliveryTag}");
                            await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: subscribeResult.IsRequeue, cancellationToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 异常处理：拒绝消息并入死信，避免消息循环
                    await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, cancellationToken);
                    _logger.Error($"RabbitMQ消费消息异常：队列[{queue.QueueName}]，DeliveryTag：{args.DeliveryTag}，异常：{ex.Message}", ex);
                }
            };

            // 绑定消费者关闭事件（监控消费者状态）
            consumer.ShutdownAsync += (sender, args) =>
            {
                _logger.Warning($"RabbitMQ消费者已关闭：队列[{queue.QueueName}]，原因：{args.ReplyText}");
                return Task.CompletedTask;
            };

            string consumerTag = await channel.BasicConsumeAsync(
                queue: queue.QueueName,
                autoAck: queue.IsAutoAck,
                consumer: consumer,
                cancellationToken: cancellationToken);

            _logger.Info($"RabbitMQ订阅成功：队列[{queue.QueueName}]，消费者Tag：{consumerTag}");

            // 取消令牌绑定：停止消费时清理资源
            cancellationToken.Register(async () =>
            {
                if (channel.IsOpen)
                {
                    await channel.BasicCancelAsync(consumerTag, false, cancellationToken);
                    _logger.Info($"RabbitMQ取消订阅：队列[{queue.QueueName}]，消费者Tag：{consumerTag}");
                }
            });
        }

        public virtual async Task RedisSubscribeAsync(T vivMessage, CancellationToken cancellationToken = default)
        {
            var queue = vivMessage.GetQueue();
            if (queue is null) return;

            _redisChannel = RedisChannel.Pattern(queue.QueueName);
            await _redisService.Value.SubscribeAsync<NanaMessage<T>>(_redisChannel, async (message) =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.Warning($"Redis消费被取消：频道[{_redisChannel}]");
                    return;
                }

                try
                {
                    if (message == null)
                    {
                        _logger.Error($"Redis消息反序列化失败：频道[{_redisChannel}]：消息内容：{message.ToJson()}");
                        return;
                    }

                    var result = await InvokeReceiveMessage(message, RetryCount, false, cancellationToken);
                    if (!result.IsSuccess)
                    {

                        _logger.Error($"Redis消息处理失败：频道[{_redisChannel}]：消息内容：{message.ToJson()}, 失败原因：{result.Message}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"Redis订阅消息处理异常：频道[{_redisChannel}]，异常：{ex.Message}", ex);
                }
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
