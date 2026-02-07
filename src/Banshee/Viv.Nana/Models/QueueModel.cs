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
        public QueueBase Queue { get; set; } = new QueueBase();

        public QueueDeclare QueueDeclare { get; set; } = new QueueDeclare();

        public ExchangeDeclare ExchangeDeclare { get; set; } = new ExchangeDeclare();
    }

    public class QueueBase
    {
        /// <summary>
        /// 交换机名称
        /// </summary>
        public string Exchange { get; set; } = string.Empty;

        /// <summary>
        /// 队列名称(主)
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 路由键
        /// </summary>
        public string RoutingKey { get; set; } = string.Empty;

        /// <summary>
        /// 交换机类型
        /// </summary>
        public string ExchangeType { get; set; } = string.Empty;

        /// <summary>
        /// 声明交换机与队列绑定的其他参数
        /// </summary>
        public Dictionary<string, object?>? Arguments { get; set; }

        /// <summary>
        /// 是否是延迟队列
        /// </summary>
        public bool IsDelayQueue { get; set; } = false;

        /// <summary>
        /// 如果为true,消息不能路由到指定的队列时,会触发channel.BasicReturn事件,如果为false,则broker会直接将消息丢弃(默认True)
        /// </summary>
        public bool IsMandatory { get; set; } = true;
    }

    public class QueueDeclare
    {
        /// <summary>
        /// QueueDeclare:声明队列是否持久化(默认True)
        /// </summary>
        public bool IsDurable { get; set; } = true;

        /// <summary>
        /// QueueDeclare:声明队列是否排他(默认False)
        /// </summary>
        public bool IsExclusive { get; set; } = false;

        /// <summary>
        /// QueueDeclare:声明队列是否自动删除(默认False)
        /// </summary>
        public bool IsAutoDelete { get; set; } = false;

        /// <summary>
        /// QueueDeclare:声明队列其他参数(可以为NULL)
        /// </summary>
        public Dictionary<string, object?>? Arguments { get; set; }

    }

    public class ExchangeDeclare
    {
        /// <summary>
        /// ExchangeDeclare:声明交换机是否持久化(默认True)
        /// </summary>
        public bool IsDurable { get; set; } = true;

        /// <summary>
        /// ExchangeDeclare:声明交换机是否自动删除(默认False)
        /// </summary>
        public bool IsAutoDelete { get; set; } = false;

        /// <summary>
        /// ExchangeDeclare:声明交换机其他参数(可以为NULL),详情参考RegisterCenter.BindService的备注
        /// </summary>
        public Dictionary<string, object?>? Arguments { get; set; }
    }
}
