using System;

namespace Viv.Nana.Enums
{
    /// <summary>
    /// 消息队列类型枚举
    /// </summary>
    public enum MessageQueueType
    {
        /// <summary>
        /// 没有
        /// </summary>
        None,

        /// <summary>
        /// RabbitMQ消息队列
        /// </summary>
        RabbitMQ,

        /// <summary>
        /// Redis发布订阅（消息通知）
        /// </summary>
        RedisPubSub,

        /// <summary>
        /// 本地消息
        /// </summary>
        LocalMessage
    }
}