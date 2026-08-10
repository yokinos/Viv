using System;

namespace Viv.Nana
{
    /// <summary>
    /// 消费并发/预取调优特性 — 标在 <see cref="VivConsumer{T}"/> 子类上，框架扫描时读取并应用到该消费者对应的队列监听。
    /// 特性缺席时回落到框架默认值。
    /// 注意：ConsumerCount &gt; 1 会失去同一队列内的严格消息顺序（RabbitMQ 多通道轮询分发），
    /// 业务侧"只执行一次"仍需用 Redis 分布式锁保证（框架只负责广播）。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class NanaConsumerAttribute : Attribute
    {
        /// <summary>
        /// 框架默认每通道预取数（basic.qos）。Wolverine 原生默认 100，框架收敛到 20 —— 更低的重投放大。
        /// </summary>
        public const ushort DefaultPrefetchCount = 20;

        /// <summary>
        /// 队列消费通道数（每实例）。0=框架默认（1，严格有序）；&gt;1 并行消费但丢失顺序；
        /// 多服务实例共享队列时总消费通道数 = ConsumerCount × 实例数。
        /// </summary>
        public int ConsumerCount { get; set; }

        /// <summary>
        /// 每通道预取未确认消息上限（basic.qos）。0=框架默认 <see cref="DefaultPrefetchCount"/>（20）。
        /// 该值同时决定崩溃时最多被重投的消息数 = ConsumerCount × PrefetchCount（跨实例再乘实例数）。
        /// </summary>
        public ushort PrefetchCount { get; set; }

        /// <summary>
        /// 端点最大并行处理消息数。0=框架默认（12，Wolverine 原生）。
        /// </summary>
        public int MaximumParallelMessages { get; set; }
    }
}
