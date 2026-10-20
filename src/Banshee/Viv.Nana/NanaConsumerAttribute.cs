using System;

namespace Viv.Nana
{
    /// <summary>
    /// 消费者队列接线特性，标在 <see cref="VivConsumer{T}"/> 子类上，框架扫描时应用到该消费者对应的队列监听。
    /// 特性缺席时用框架默认值。
    ///
    /// 三个数值属性与 <see cref="Topology"/> 都落到 <c>RabbitMqQueue</c> 上，是这个消费者接线方式的唯一声明点。
    /// <see cref="ConsumerCount"/> &gt; 1 会失去同一队列内的严格顺序（RabbitMQ 多通道轮询分发）；
    /// 同服务只执行一次由 <see cref="VivConsumer{T}"/> 基类取 Redis 锁保证。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class NanaConsumerAttribute : Attribute
    {
        /// <summary>
        /// 框架默认每通道预取数（basic.qos）。Wolverine 原生默认 100，这里收敛到 20 以降低重投放大。
        /// </summary>
        public const ushort DefaultPrefetchCount = 20;

        /// <summary>
        /// 订阅拓扑，默认 <see cref="ConsumerTopology.Shared"/>。
        /// 设为 <see cref="ConsumerTopology.PerInstance"/> 时不能指定 <see cref="ConsumerCount"/> 与
        /// <see cref="MaximumParallelMessages"/>：多监听或多并行会打乱单连接顺序。注册期抛。
        /// </summary>
        public ConsumerTopology Topology { get; set; } = ConsumerTopology.Shared;

        /// <summary>
        /// 队列消费通道数（每实例）。0=框架默认 1（严格有序），&gt;1 并行但丢失顺序。
        /// 多实例共享队列时总通道数 = ConsumerCount × 实例数。仅 Shared 下有效。
        /// </summary>
        public int ConsumerCount { get; set; }

        /// <summary>
        /// 每通道预取未确认消息上限（basic.qos）。0=框架默认 <see cref="DefaultPrefetchCount"/>。
        /// 崩溃时最多被重投的消息数 = ConsumerCount × PrefetchCount（跨实例再乘实例数）。
        /// </summary>
        public ushort PrefetchCount { get; set; }

        /// <summary>
        /// 端点最大并行处理消息数。0=框架默认 12（Wolverine 原生）。仅 Shared 下有效；
        /// PerInstance 固定 1，不设等于没有顺序保证。
        /// </summary>
        public int MaximumParallelMessages { get; set; }
    }
}
