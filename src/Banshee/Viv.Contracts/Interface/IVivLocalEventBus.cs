using System;
using System.Threading;
using System.Threading.Tasks;
using Viv.Contracts.Events;

namespace Viv.Contracts.Interface
{
    /// <summary>
    /// 本地事件总线 —— 进程内「解耦的同步调用」。
    ///
    /// 【与跨进程事件的区别】
    /// 跨进程走 <c>IVivEventPublisher</c>（Viv.Nana）：事件出网、消费端是另一个进程、
    /// 也是另一个 DI 作用域。本地事件不出网，handler 与发布方**同一个 DI 作用域** ——
    /// 注入的 IMomoDbContext / IVivContext 就是发布方那一个，写操作与主业务同批提交。
    ///
    /// 【分发时机】
    /// <see cref="PublishAsync{TEvent}"/> 只入队，不立即执行。
    /// 真正分发推迟到作用域正常结束时（各宿主触发点：MVC 过滤器 / 兜底中间件），
    /// 保证 handler 看到的是「最终定格」的数据状态，而不是走到一半的半成品。
    /// 作用域异常退出 → 整队丢弃，一条事件都不发（不会出现「库回滚了但事件已经出去」的幽灵事件）。
    ///
    /// 【事件类型约束】
    /// 事件必须继承 <see cref="EngineEvent"/> —— 空标记基类，纯限制：本地事件的 handler 是业务的
    /// 一部分，写下来就必须执行，所以事件类型不允许随手写。约束在编译期生效，
    /// <c>PublishAsync(new object())</c> 编译不过。
    ///
    /// 与 NanaEvent 是**二选一的两条路**：要跨进程继承 NanaEvent 走 IVivEventPublisher，
    /// 只在进程内继承 EngineEvent 走本接口。**切勿让 EngineEvent 继承 NanaEvent** ——
    /// Wolverine 对每个 NanaEvent 子类都注册了 RabbitMQ 路由（VivWolverineConfigurationExtensions），
    /// 继承即被绑死成跨进程语义。
    ///
    /// 【已知缺口】
    /// 当前触发点只在 HTTP 路径上。Worker / 消息消费 / TickerQ 定时任务里发布的事件
    /// 不会被自动分发，只会由作用域结束时记录一条「未分发」Warning。
    /// </summary>
    public interface IVivLocalEventBus
    {
        /// <summary>
        /// 发布本地事件 —— 只入队，不立即执行。
        /// 入参为 null 抛 <see cref="ArgumentNullException"/>。
        /// </summary>
        /// <typeparam name="TEvent">事件类型，须继承 <see cref="EngineEvent"/></typeparam>
        /// <param name="event">事件实例</param>
        /// <param name="ct">取消令牌</param>
        Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : EngineEvent;

        /// <summary>
        /// 分发全部待发事件，按入队顺序执行各事件的处理器。
        /// 幂等：重复调用只有第一次生效。
        /// <b>框架调用（HTTP 过滤器 / 兜底中间件），业务代码不要直接调用。</b>
        /// 框架触发点一律传 <see cref="CancellationToken.None"/> —— 处理器是业务的一部分，不因客户端断开而跳过。
        /// </summary>
        /// <param name="ct">取消令牌</param>
        Task FlushAsync(CancellationToken ct = default);

        /// <summary>
        /// 丢弃全部待发事件（业务失败时由框架调用，事件一条都不发）。
        /// 幂等。
        /// <b>框架调用，业务代码不要直接调用。</b>
        /// </summary>
        void Discard();
    }
}
