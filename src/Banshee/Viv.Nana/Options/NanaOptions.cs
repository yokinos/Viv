using System;
using Viv.Nana.Enums;

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
        /// 备用/副消息队列类型（默认未启用）
        /// </summary>
        public MessageQueueType SecondaryQueueType { get; set; } = MessageQueueType.None;

        /// <summary>
        /// 是否启用本地消息模式
        /// 启用时需实现 <see cref="Interface.ILocalMessageRespository"/> 接口
        /// </summary>
        public bool IsEnableLocalMessage { get; set; } = false;

        /// <summary>
        /// 主队列发布失败的重试次数（默认3次）
        /// </summary>
        public int RetryCount { get; set; } = 3;

        /// <summary>
        /// RabbitMQ 连接及配置项
        /// </summary>
        public RabbitMQOptions? RabbitMQOptions { get; set; }
    }
}