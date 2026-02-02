using System;
using Viv.Vva.Magic;

namespace Viv.RabbitMQ.Model
{
    /// <summary>
    /// RabbitMQ 通用队列消息体
    /// 所有业务队列消息统一使用该类封装，T为具体业务数据类型
    /// </summary>
    /// <typeparam name="T">业务消息内容类型（如订单DTO、用户事件实体等），限制为引用类型</typeparam>
    public class MQMessage<T> where T : class
    {
        /// <summary>
        /// 构造函数：自动生成唯一消息标识Id，初始化公共字段
        /// </summary>
        public MQMessage()
        {
            Id = IdMagic.NextId();
        }

        /// <summary>
        /// 扩展构造函数：支持手动传入业务内容，简化对象创建
        /// </summary>
        /// <param name="content">业务消息内容</param>
        /// <exception cref="ArgumentNullException">消息内容不能为空</exception>
        public MQMessage(T content) : this()
        {
            ArgumentNullException.ThrowIfNull(content);
            Content = content;
        }

        /// <summary>
        /// Viv项目统一AppId（标识消息所属应用）
        /// </summary>
        public long VivAppId { get; set; }

        /// <summary>
        /// 消息全局唯一标识（由IdMagic自动生成，不可修改）
        /// 用于消息幂等处理、轨迹追踪、重试标识
        /// </summary>
        public long Id { get; private set; }

        /// <summary>
        /// 消息内容
        /// </summary>
        public T Content { get; set; }
    }
}