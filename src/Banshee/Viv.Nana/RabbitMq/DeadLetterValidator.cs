using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Log;
using Viv.Nana.Models;
using Viv.Vva.Magic;

namespace Viv.Nana.RabbitMq
{
    /// <summary>
    /// 死信队列验证器
    /// </summary>
    public class DeadLetterValidator
    {
        public static bool Validate(QueueModel model, QueueModel deadLetter)
        {
            if (model == null || deadLetter == null) return false;

            if (!model.QueueDeclare.Arguments.TryGetValue("x-dead-letter-exchange", out var x_dead_latter_exchange_obejct)
                || !model.QueueDeclare.Arguments.TryGetValue("x-dead-letter-routing-key", out var x_dead_latter_routing_key_object))
            {
                VivWriteLogger.Error("死信配置错误,需检查DeclareQueueArguments,有信息缺失,需含有key:x-dead-letter-exchange与x-dead-letter-routing-key");
                return false;
            }

            if (x_dead_latter_exchange_obejct == null) return false;

            var x_dead_latter_exchange = x_dead_latter_exchange_obejct.ToString();
            var x_dead_latter_routing_key = x_dead_latter_routing_key_object?.ToString() ?? string.Empty;

            if (x_dead_latter_exchange != deadLetter.Exchange)
            {
                VivWriteLogger.Error("死信配置错误,x-dead-letter-exchange与DeadLetterExchange不一致");
                return false;
            }

            //检查路由键是否匹配
            if (model.ExchangeType == ExchangeType.Direct)
            {
                if (x_dead_latter_routing_key != deadLetter.RoutingKey)
                {
                    VivWriteLogger.Error("死信交换机类型为Direct:x-dead-letter-routing-key与DeadLetterRoutingKey需一致");
                    return false;
                }
            }

            //检查路由键是否符合规则
            if (deadLetter.ExchangeType == ExchangeType.Topic)
            {
                //若设置死信交换机为Topic,建议死信路由键以"#"结尾
                if (deadLetter.RoutingKey.EndsWith('#'))
                {
                    if (!x_dead_latter_routing_key.Contains(deadLetter.RoutingKey.Replace("#", "").Trim()))
                    {
                        VivWriteLogger.Error("死信交换机类型为Topic,路由键类型:#,DeadLetterRoutingKey设置错误,请参考文档重新设置");
                        return false;
                    }
                }
                else if (deadLetter.RoutingKey.EndsWith('*'))
                {
                    if (!x_dead_latter_routing_key.Contains(deadLetter.RoutingKey.Replace("*", "").Trim()))
                    {
                        VivWriteLogger.Error("死信交换机类型为Topic,路由键类型:*,DeadLetterRoutingKey设置错误,请参考文档重新设置");
                        return false;
                    }
                    else
                    {
                        var terms = x_dead_latter_routing_key.Replace(deadLetter.RoutingKey.Replace("*", "").Trim(), "");
                        if (!StringMagic.IsEnglish(terms))
                        {
                            VivWriteLogger.Error("死信交换机类型为Topic,路由键类型:*,DeadLetterRoutingKey设置错误,只能有一个单词,请参考文档重新设置");
                            return false;
                        }
                    }
                }
                else
                {
                    //路由键不以"#"或者"*"结尾,需判断其是否匹配
                    if (x_dead_latter_routing_key != deadLetter.RoutingKey)
                    {
                        VivWriteLogger.Error("死信交换机类型为Topic:路由键不以#或者*结尾,需判断其是否匹配,x-dead-letter-routing-key与DeadLetterRoutingKey需一致");
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
