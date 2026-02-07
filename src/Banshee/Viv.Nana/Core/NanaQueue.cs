using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Viv.Nana.Enums;
using Viv.Nana.Models;
using Viv.Vva.Magic;

namespace Viv.Nana.Core
{
    /// <summary>
    /// RabbitMQ消息队列的消息模型基类
    /// 核心作用：统一封装消息的基础属性、队列配置、交换机配置，子类只需继承即可自动适配配置
    /// 设计规范：子类命名需以"Message"结尾（如OrderMessage），队列名自动生成（如OrderQueue）
    /// </summary>
    public abstract class NanaMessage
    {
        private const string MessageEnd = "Message";
        private const string QeueuEnd = "Queue";
        private const string DefaultExchange = "Viv.Exchange.Default";
        private const string DefaultDeadLetterExchange = "Viv.Exchange.DeadLetter";

        /// <summary>
        /// 获取RabbitMQ消息基础属性
        /// </summary>
        /// <returns>消息属性对象（IBasicProperties的实现类），默认返回null</returns>
        [return: MaybeNull]
        public virtual BasicProperties GetBasicProperties()
        {
            return default;
        }

        /// <summary>
        /// 自动生成队列基础配置
        /// </summary>
        /// <returns></returns>
        [return: MaybeNull]
        public virtual QueueBase GetQueue()
        {
            var type = GetType();
            if (!type.Name.EndsWith(MessageEnd))
            {
                return null;
            }

            var baseName = StringMagic.RemoveEnd(type.Name, MessageEnd);
            var queueName = $"{baseName}{QeueuEnd}";
            var routingkey = GetRoutingkey(queueName);
            var deadLetterRoutingKey = GetDeadLetterRoutingkey(queueName);

            var queue = new QueueBase()
            {
                Name = queueName,
                Exchange = type.Namespace ?? DefaultExchange,
                ExchangeType = ExchangeType.Direct,
                RoutingKey = routingkey,
                IsDelayQueue = false,
                Arguments = new Dictionary<string, object?>()
                {
                    { "x-dead-letter-exchange", DefaultDeadLetterExchange },
                    { "x-dead-letter-routing-key", deadLetterRoutingKey },
                    { "x-message-ttl", 86400000 }
                }
            };

            return queue;
        }

        /// <summary>
        /// 获取路由键
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public virtual string GetRoutingkey(string queueName)
        {
            var routingkey = $"viv.{queueName}.key";
            return routingkey;
        }

        /// <summary>
        /// 获取死信路由键
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public virtual string GetDeadLetterRoutingkey(string queueName)
        {
            var routingkey = $"viv.{queueName}.deadletter";
            return routingkey;
        }

        /// <summary>
        /// 获取队列声明的详细配置
        /// </summary>
        /// <returns</returns>
        [return: MaybeNull]
        public virtual QueueDeclare GetQueueDeclare()
        {
            return new QueueDeclare() { Arguments = [] };
        }

        /// <summary>
        /// 获取交换机声明的详细配置
        /// </summary>
        /// <returns></returns>
        [return: MaybeNull]
        public virtual ExchangeDeclare GetExchangeDeclare()
        {
            return new ExchangeDeclare() { Arguments = [] };
        }
    }
}