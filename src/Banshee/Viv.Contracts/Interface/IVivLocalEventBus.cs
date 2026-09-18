using System;
using System.Threading;
using System.Threading.Tasks;
using Viv.Contracts.Events;

namespace Viv.Contracts.Interface
{
    /// <summary>
    /// 本地事件总线 —— 进程内解耦的同步调用。
    ///
    /// 与跨进程事件的区别：跨进程走 <c>IVivEventPublisher</c>，事件出网、消费端是另一个进程、
    /// 也是另一个 DI 作用域；本地事件不出网，handler 与发布方同一个 DI 作用域 ——
    /// 注入的 IMomoDbContext / IVivContext 就是发布方那一个，写操作与主业务同批提交。
    ///
    /// 分发时机：<see cref="PublishAsync{TEvent}"/> 只入队，真正分发推迟到作用域正常结束时
    /// （触发点见 MVC 过滤器 / 兜底中间件 / 消费者 <c>HandleAsync</c>），保证 handler 看到的是最终定格的数据状态。
    /// 作用域异常退出则整队丢弃，不会出现「库回滚了但事件已经出去」。
    ///
    /// 事件类型约束：必须继承 <see cref="LocalEvent"/>，约束在编译期生效（<c>PublishAsync(new object())</c> 编译不过）。
    /// 与 NanaEvent 是二选一的两条路，二者不可互相继承。
    ///
    /// 触发点现状：HTTP（过滤器 / 中间件）与消息消费（<c>VivConsumer</c> / <c>VivLocalConsumer</c> 的
    /// <c>HandleAsync</c>）都已覆盖。TickerQ 定时任务与业务手写的 BackgroundService 无框架级统一入口，
    /// 仍未覆盖 —— 那些场景入队的事件不会被分发，只由作用域结束时记一条「未分发」Warning。
    /// </summary>
    public interface IVivLocalEventBus
    {
        /// <summary>
        /// 发布本地事件 —— 只入队，不立即执行。
        /// 入参为 null 抛 <see cref="ArgumentNullException"/>。
        /// </summary>
        /// <typeparam name="TEvent">事件类型，须继承 <see cref="LocalEvent"/></typeparam>
        /// <param name="event">事件实例</param>
        /// <param name="ct">取消令牌</param>
        Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : LocalEvent;

        /// <summary>
        /// 分发全部待发事件，按入队顺序执行各事件的处理器。幂等，重复调用只有第一次生效。
        /// 框架调用（HTTP 过滤器 / 兜底中间件 / 消费者 HandleAsync），业务代码不要直接调用。
        /// 触发点一律传 <see cref="CancellationToken.None"/> —— 处理器是业务的一部分，不因客户端断开而跳过。
        /// 处理器抛出的异常直接上抛，不吞：静默失败等于「业务以为通知发了、其实没发」。
        /// </summary>
        /// <param name="ct">取消令牌</param>
        Task FlushAsync(CancellationToken ct = default);

        /// <summary>
        /// 丢弃全部待发事件（业务失败时由框架调用，事件一条都不发）。幂等。
        /// 框架调用，业务代码不要直接调用。
        /// </summary>
        void Discard();
    }
}
