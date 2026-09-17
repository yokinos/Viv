using System;
using Viv.Contracts.Models;
using Viv.Delusion.Magic;
using Viv.Nana.Core;

namespace Viv.Nana
{
    /// <summary>
    /// 本地队列信封 —— 与 <see cref="NanaEnvelope{T}"/> 同形，但约束指向 <see cref="NanaLocalEvent"/>。
    ///
    /// 之所以要单独一个信封：<see cref="NanaEnvelope{T}"/> 的约束写死 <c>where T : NanaEvent</c>，改不得，
    /// 而本地事件按设计**不是** NanaEvent 的子类（见 <see cref="NanaLocalEvent"/> 的注释）。
    ///
    /// 比 <see cref="NanaEnvelope{T}"/> 少 <c>DelaySecond</c> / <c>ReDeliverCount</c> —— 本版不做延迟重投，
    /// 延迟本身由 Wolverine 的 <c>DeliveryOptions.ScheduleDelay</c> 承载，不需要落在信封上。
    /// </summary>
    /// <typeparam name="T">本地事件类型</typeparam>
    public class NanaLocalEnvelope<T> where T : NanaLocalEvent
    {
        public NanaLocalEnvelope() { }

        /// <summary>
        /// 消息Id 也就是邮戳的了
        /// </summary>
        public long MessageId { get; set; } = IdMagic.NextId();

        /// <summary>
        /// Viv的上下文信息。
        /// <b>不是可选项</b>：消费者跑在后台线程 + 独立 DI 作用域上，AsyncLocal 里的租户上下文不会跟过去，
        /// 而 EFAppContext 的全局查询过滤器在「无上下文」时**不过滤**，等于跨租户读。
        /// 必须靠这个快照在消费端水合。
        /// </summary>
        public VivContextContent? Context { get; set; }

        /// <summary>
        /// 消息内容
        /// </summary>
        public T? Content { get; set; }

        /// <summary>
        /// 消息创建时间
        /// </summary>
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
