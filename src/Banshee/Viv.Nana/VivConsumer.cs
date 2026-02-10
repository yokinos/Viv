using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Log.VivLogger;
using Viv.Nana.Core;
using Viv.Nana.Enums;
using Viv.Nana.Models;
using Viv.Nana.RabbitMq;
using Viv.Redis;

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
    public abstract class VivConsumer<T> : NanaFactory where T : VivMessage, new()
    {
        protected VivConsumer(IVivLogger logger, Lazy<IRedisService> redisService) : base(logger, redisService)
        {

        }

        /// <summary>
        /// 接收消息
        /// </summary>
        /// <param name="message"></param>
        /// <param name="isDeadLetter"></param>
        /// <returns></returns>
        public abstract Task<SubscribeResult> ReceiveMessageAsync(NanaMessage<T> message, bool isDeadLetter = false);

        /// <summary>
        /// 每次拉取的消息数量（1-100）
        /// </summary>
        public virtual int PullCount { get; set; } = 1;

        /// <summary>
        /// 重试次数
        /// 针对RabbitMQ，失败后重试次数，超过后进入死信队列；
        /// 针对Redis Pub/Sub，失败后重试次数，超过后降级到本地消息表；
        /// 针对本地消息表，失败后重试次数，超过后记录日志等待人工干预
        /// </summary>
        public virtual int RetryCount { get; set; } = 3;

        /// <summary>
        /// 订阅消息
        /// </summary>
        /// <returns></returns>
        public virtual async Task SubscribeAsync(CancellationToken cancellationToken)
        {
            var vivMessage = new T();
            await RabbitMQSubscribeAsync(vivMessage, cancellationToken);
            await RedisSubscribeAsync();
        }

        /// <summary>
        /// 订阅消息
        /// </summary>
        /// <returns></returns>
        public virtual async Task RabbitMQSubscribeAsync(T vivMessage, CancellationToken cancellationToken = default)
        {
            var queue = GetQueue(vivMessage);
            if (queue is null) return;

            var channel = VivRabbitClient.GetInstance().GetChannelAsync(queue, null, cancellationToken);
            if (channel is null) return;


        }

        /// <summary>
        /// 订阅消息
        /// </summary>
        /// <returns></returns>
        public virtual async Task RedisSubscribeAsync()
        {

        }
    }
}
