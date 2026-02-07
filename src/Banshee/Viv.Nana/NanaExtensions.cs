using System;
using System.Collections.Generic;
using System.Text;
using RabbitMQ.Client;
using Viv.Nana.Enums;

namespace Viv.Nana
{
    public static class NanaExtensions
    {
        /// <summary>
        /// 将枚举转换为RabbitMQ官方交换机类型字符串（如Direct→"direct"）
        /// </summary>
        /// <param name="exchangeType">交换机枚举</param>
        /// <returns>官方字符串（匹配RabbitMQ.Client.ExchangeType）</returns>
        /// <exception cref="ArgumentOutOfRangeException">不支持的交换机类型</exception>
        public static string ToOfficialString(this RabbitMQExchange exchangeType)
        {
            return exchangeType switch
            {
                RabbitMQExchange.Direct => ExchangeType.Direct,
                RabbitMQExchange.Topic => ExchangeType.Topic,
                RabbitMQExchange.Fanout => ExchangeType.Fanout,
                RabbitMQExchange.Headers =>ExchangeType.Headers,
                _ => throw new ArgumentOutOfRangeException(nameof(exchangeType), $"不支持的交换机类型：{exchangeType}")
            };
        }
    }
}
