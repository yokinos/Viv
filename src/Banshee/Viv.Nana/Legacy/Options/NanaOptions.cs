using System;
using Viv.Nana.Enums;
using Viv.Vva.Magic;

namespace Viv.Nana.Options
{
    /// <summary>
    /// Nana消息队列核心配置项
    /// </summary>
    public class NanaOptions
    {
        /// <summary>
        /// 主消息队列类型（默认RabbitMQ）
        /// </summary>
        public MessageQueueType MainQueueType { get; set; } = MessageQueueType.RabbitMQ;

        /// <summary>
        /// 备用/副消息队列类型
        /// </summary>
        public MessageQueueType SecondaryQueueType { get; set; } = MessageQueueType.RedisPubSub;

        /// <summary>
        /// 是否启用本地消息模式
        /// 启用时需实现 <see cref="LocalMessage.ILocalMessageRespository"/> 接口
        /// </summary>
        public bool IsEnableLocalMessage { get; set; } = false;

        /// <summary>
        /// 主队列发布失败的重试次数（默认3次）
        /// </summary>
        public int RetryCount { get; set; } = 3;

        /// <summary>
        /// RabbitMQ 连接及配置项
        /// </summary>
        public RabbitMqOptions? RabbitMqOptions { get; set; }

        /// <summary>
        /// 要注册的消费者类型
        /// </summary>
        public List<FilterTypeOptions> ConsumerTypes { get; set; } = [];
    }
}