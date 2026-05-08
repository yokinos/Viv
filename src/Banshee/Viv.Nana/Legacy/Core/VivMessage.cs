using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Viv.Nana.Models;
using Viv.Vva.Magic;

namespace Viv.Nana.Core
{
    /// <summary>
    /// RabbitMQ消息队列的消息模型基类
    /// 核心作用：统一封装消息的基础属性、队列配置、交换机配置，子类只需继承即可自动适配配置
    /// 设计规范：子类命名需以"Message"结尾（如OrderMessage），队列名自动生成（如OrderQueue）
    /// </summary>
    public abstract class VivMessage
    {
        // 命名规范：消息类命名需以"Message"结尾，队列名自动生成
        private const string MessageEnd = "Message";
        private const string QeueuEnd = "Queue";
        // 死信队列后缀
        private const string DeadLetterQueueEnd = "DeadLetterQueue";

        // 默认的路由键和交换机格式，使用占位符{0}自动替换为队列名
        private const string DefaultRoutingKey = "Viv.{0}.Key";
        private const string DefaultExchange = "Viv.Exchange.Default";

        // 死信队列相关配置，死信交换机和死信路由键格式
        private const string DefaultDeadLetterExchange = "Viv.Exchange.DeadLetter";
        private const string DefaultDeadLetterRoutingKey = "Viv.{0}.Deadletter";

        // Delayed Exchange插件相关常量
        private const string DelayedExchangeType = "x-delayed-message";
        private const string DelayedExchangeArgKey = "x-delayed-type";

        public VivMessage() : this(false) { }

        public VivMessage(bool isDelayQueue, TimeSpan? delayTTL = null)
        {
            IsDelayQueue = isDelayQueue;
            if (isDelayQueue)
            {
                DelayTTL = delayTTL ?? TimeSpan.FromSeconds(30);
            }
        }

        /// <summary>
        /// 是否是延迟队列
        /// </summary>
        /// <returns></returns>
        public bool IsDelayQueue { get; private set; }

        /// <summary>
        /// 延迟多久（仅对延迟队列有效）
        /// </summary>
        public TimeSpan DelayTTL { get; set; }

        /// <summary>
        /// RabbitMQ已经按照AMQP规范实现了priority字段,它的值被定义为0~9之间.用于指定队列中消息的优先级.
        /// priority字段实现为无符号字节,所以优先级可以是0~255,但优先级应该被限制在0~9之间.
        /// </summary>
        public byte Priority { get; set; }

        /// <summary>
        /// 获取RabbitMQ消息基础属性
        /// </summary>
        /// <returns>消息属性对象（IBasicProperties的实现类）</returns>
        [return: MaybeNull]
        public virtual BasicProperties GetBasicProperties(long vivAppId, long messageId)
        {
            var properties = new BasicProperties()
            {
                AppId = vivAppId.ToString(),
                ContentType = "application/json",
                ContentEncoding = "utf-8",
                MessageId = messageId.ToString(),
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                DeliveryMode = DeliveryModes.Persistent,
                UserId = "VivNana",
                Headers = new Dictionary<string, object?>()
            };

            if (IsDelayQueue)
            {
                properties.Headers.Add("x-delay", DelayTTL.TotalMilliseconds);
            }

            return properties;
        }

        /// <summary>
        /// 自动生成队列基础配置
        /// </summary>
        /// <returns></returns>
        [return: MaybeNull]
        public virtual QueueModel GetQueue()
        {
            var queueName = GetQueueName();
            if (string.IsNullOrEmpty(queueName)) return default;

            var exchangeType = GetExchangeType();
            var exchangeDeclare = GetExchangeDeclare();
            if (IsDelayQueue)
            {
                exchangeDeclare.Arguments[DelayedExchangeArgKey] = exchangeType;
                exchangeType = DelayedExchangeType;
            }

            var queue = new QueueModel()
            {
                QueueName = GetQueueName(),
                Exchange = GetExchange(),
                ExchangeType = exchangeType,
                RoutingKey = GetRoutingkey(queueName),
                IsMandatory = true,
                QueueBind = GetQueueBind(queueName),
                ExchangeDeclare = exchangeDeclare,
                QueueDeclare = GetQueueDeclare(),
            };

            return queue;
        }

        /// <summary>
        /// 获取死信队列配置
        /// </summary>
        /// <returns>死信队列的完整配置模型，若主队列名无效则返回null</returns>
        [return: MaybeNull]
        public virtual QueueModel GetDeadLetterQueue()
        {
            var mainQueueName = GetQueueName();
            if (string.IsNullOrEmpty(mainQueueName)) return default;

            var deadLetterQueueName = GetDeadLetterQueueName(mainQueueName);
            if (string.IsNullOrEmpty(deadLetterQueueName)) return default;

            var deadLetterQueue = new QueueModel()
            {
                QueueName = deadLetterQueueName,
                Exchange = GetDeadLetterExchange(),
                ExchangeType = GetDeadLetterExchangeType(),
                RoutingKey = GetDeadLetterRoutingkey(mainQueueName),
                IsMandatory = true,
                QueueBind = new QueueBind(),
                ExchangeDeclare = new ExchangeDeclare(),
                QueueDeclare = new QueueDeclare(),
            };

            return deadLetterQueue;
        }

        /// <summary>
        /// 生成死信队列名称（主队列名_Error）
        /// </summary>
        /// <param name="mainQueueName">主队列名称</param>
        /// <returns>死信队列名称</returns>
        protected virtual string GetDeadLetterQueueName(string mainQueueName)
        {
            return $"{StringMagic.RemoveEnd(mainQueueName, QeueuEnd)}.{DeadLetterQueueEnd}";
        }

        /// <summary>
        /// 获取队列名称，默认根据类名自动生成（如OrderMessage对应OrderQueue），子类可重写以适配不同命名规范
        /// </summary>
        /// <returns></returns>
        public virtual string GetQueueName()
        {
            var type = GetType();
            if (!type.Name.EndsWith(MessageEnd))
            {
                return string.Empty;
            }
            var baseName = StringMagic.RemoveEnd(type.Name, MessageEnd);
            var queueName = $"{baseName}{QeueuEnd}";
            return queueName;
        }

        /// <summary>
        /// 获取交换机类型，默认Direct
        /// <see cref="ExchangeType"/>
        /// </summary>
        /// <returns></returns>
        public virtual string GetExchangeType()
        {
            return ExchangeType.Direct;
        }

        /// <summary>
        /// 获取死信交换机类型
        /// <see cref="ExchangeType"/>
        /// </summary>
        /// <returns></returns>
        public virtual string GetDeadLetterExchangeType()
        {
            return ExchangeType.Direct;
        }

        /// <summary>
        /// 获取交换机
        /// </summary>
        /// <returns></returns>
        public virtual string GetExchange()
        {
            var type = GetType();
            var exchange = type.Namespace ?? DefaultExchange;
            return exchange;
        }

        /// <summary>
        /// 获取死信交换机
        /// </summary>
        /// <returns></returns>
        public virtual string GetDeadLetterExchange()
        {
            return DefaultDeadLetterExchange;
        }

        /// <summary>
        /// 获取路由键
        /// </summary>
        /// <param name="queueName"></param>
        /// <returns></returns>
        public virtual string GetRoutingkey(string queueName)
        {
            var routingkey = string.Format(DefaultRoutingKey, queueName);
            return routingkey;
        }

        /// <summary>
        /// 获取死信路由键
        /// </summary>
        /// <param name="queueName"></param>
        /// <returns></returns>
        public virtual string GetDeadLetterRoutingkey(string queueName)
        {
            var routingkey = string.Format(DefaultDeadLetterRoutingKey, queueName);
            return routingkey;
        }

        /// <summary>
        /// 获取队列绑定配置
        /// </summary>
        /// <param name="queueName"></param>
        /// <returns></returns>
        public virtual QueueBind GetQueueBind(string queueName)
        {
            var queueBind = new QueueBind()
            {
                Arguments = new Dictionary<string, object?>()
                {
                    { "x-dead-letter-exchange", GetDeadLetterExchange() },
                    { "x-dead-letter-routing-key", GetDeadLetterRoutingkey(queueName) },
                    { "x-message-ttl", GetMessageTTL().TotalMilliseconds }
                }
            };
            return queueBind;
        }

        /// <summary>
        /// 获取消息的存活时间（TTL），默认1天
        /// </summary>
        /// <returns></returns>
        public virtual TimeSpan GetMessageTTL()
        {
            return TimeSpan.FromDays(1);
        }

        /// <summary>
        /// 获取队列声明的详细配置
        /// </summary>
        /// <returns</returns>
        [return: NotNull]
        public virtual QueueDeclare GetQueueDeclare()
        {
            return new QueueDeclare();
        }

        /// <summary>
        /// 获取交换机声明的详细配置
        /// </summary>
        /// <returns></returns>
        [return: NotNull]
        public virtual ExchangeDeclare GetExchangeDeclare()
        {
            return new ExchangeDeclare();
        }
    }
}