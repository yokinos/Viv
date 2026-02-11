using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Nana.Models
{
    /// <summary>
    /// 队列模型
    /// </summary>
    public class QueueModel
    {
        /// <summary>
        /// 队列名称(主)
        /// </summary>
        public string QueueName { get; set; } = string.Empty;

        /// <summary>
        /// 交换机名称
        /// </summary>
        public string Exchange { get; set; } = string.Empty;

        /// <summary>
        /// 路由键
        /// </summary>
        public string RoutingKey { get; set; } = string.Empty;

        /// <summary>
        /// 交换机类型
        /// </summary>
        public string ExchangeType { get; set; } = string.Empty;

        /// <summary>
        /// 如果为true,消息不能路由到指定的队列时,会触发channel.BasicReturn事件,如果为false,则broker会直接将消息丢弃(默认True)
        /// </summary>
        public bool IsMandatory { get; set; } = true;

        /// <summary>
        /// 是否自动ACK,如果为true,消费者在收到消息后会自动发送ACK确认消息已被处理,如果为false,消费者需要手动发送ACK确认消息已被处理(默认False)
        /// </summary>
        public bool IsAutoAck { get; } = false;

        /// <summary>
        /// 队列绑定设置
        /// </summary>
        public QueueBind QueueBind { get; set; } = new QueueBind();

        /// <summary>
        /// 队列声明设置
        /// </summary>
        public QueueDeclare QueueDeclare { get; set; } = new QueueDeclare();

        /// <summary>
        /// 交换机声明设置
        /// </summary>
        public ExchangeDeclare ExchangeDeclare { get; set; } = new ExchangeDeclare();
    }

    public class QueueBind
    {
        /// <summary>
        /// 声明交换机与队列绑定的其他参数
        /// x-dead-letter-exchange
        /// x-dead-letter-routing-key
        /// x-message-ttl
        /// </summary>
        public Dictionary<string, object?>? Arguments { get; set; }
    }

    public class QueueDeclare
    {
        /// <summary>
        /// 声明队列是否持久化(默认True)
        /// </summary>
        public bool IsDurable { get; set; } = true;

        /// <summary>
        /// 声明队列是否排他(默认False)
        /// </summary>
        public bool IsExclusive { get; set; } = false;

        /// <summary>
        /// 声明队列是否自动删除(默认False)
        /// </summary>
        public bool IsAutoDelete { get; set; } = false;

        /// <summary>
        /// 声明队列其他参数(可以为NULL)
        /// x-message-ttl:发布到队列的消息在被丢弃之前可以存活多长时间(毫秒) 
        /// x-expires:队列在被自动删除(毫秒)之前可以使用多长时间
        /// x-max-length:队列在开始从其头部丢弃消息之前可以包含多少(就绪)消息
        /// x-max-length-bytes:队列在开始从其头部删除它们之前可以包含的就绪消息的总正文大小
        /// x-overflow:设置队列溢出行为,这决定了当达到队列的最大长度时消息会发生什么,有效值为drop-head,reject-publish或reject-publish-dlx,仲裁队列类型仅支持drop-head和reject-publish。
        /// x-dead-letter-exchange:死信交换机,有效值为drop-head或reject-publish。交换的可选名称，如果消息被拒绝或过期，将重新发布这些名称
        /// x-dead-letter-routing-key:死信路由键
        /// x-single-active-consumer:如果设置,确保一次只有一个消费者从队列中消费,并在活动消费者被取消或死亡的情况下故障转移到另一个注册的消费者
        /// x-max-priority:队列支持的最大优先级数;如果未设置,队列将不支持消息优先级
        /// x-queue-mode:(lazy)将队列设置为延迟模式,在磁盘上保留尽可能多的消息以减少内存使用;如果未设置,队列将保留内存缓存以尽快传递消息
        /// x-queue-master-locator:将队列设置为主位置模式,确定在节点集群上声明时队列主机所在的规则
        /// </summary>
        public Dictionary<string, object?> Arguments { get; set; } = [];
    }

    public class ExchangeDeclare
    {
        /// <summary>
        /// 声明交换机是否持久化(默认True)
        /// </summary>
        public bool IsDurable { get; set; } = true;

        /// <summary>
        /// 声明交换机是否自动删除(默认False)
        /// </summary>
        public bool IsAutoDelete { get; set; } = false;

        /// <summary>
        /// 声明交换机其他参数(可以为NULL)
        /// alternate-exchange:如果无法通过其他方式路由到此交换的消息,请将它们发送到此处指定的备用交换
        /// </summary>
        public Dictionary<string, object?> Arguments { get; set; } = [];
    }
}
