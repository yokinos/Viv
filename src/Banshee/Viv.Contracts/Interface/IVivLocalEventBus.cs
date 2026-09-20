using System;
using System.Threading;
using System.Threading.Tasks;
using Viv.Contracts.Events;

namespace Viv.Contracts.Interface
{
    /// <summary>
    /// 本地事件总线 —— 进程内解耦的同步调用（fanout，与发布方同一 DI 作用域）。
    ///
    /// 与另外三族事件的区别：
    /// 跨进程当场发走 <c>IVivEventPublisher</c>（出网、另一个进程、另一个 DI 作用域）；
    /// 跨进程且与写库原子走发件箱 <c>IVivOutbox</c>（入队与业务写同一本地事务，投递在提交之后）；
    /// 进程内异步点对点走 <c>IVivLocalEventPublisher</c>（Wolverine 本地队列，独立 DI 作用域，与数据库事务无关）；
    /// 本接口是进程内同步 fanout：handler 与发布方同一个 DI 作用域，注入的 IMomoDbContext / IVivContext
    /// 就是发布方那一个，但 <b>handler 并不跟主业务写在同一个数据库事务里提交</b>。
    ///
    /// 分发时机：<see cref="PublishAsync{TEvent}"/> 只入队，真正分发推迟到本次请求 / 本次消息消费
    /// <b>成功结束之后</b>（触发点见 MVC 过滤器 / 兜底中间件 / 消费者 HandleAsync /
    /// <see cref="IVivLocalEventScope"/>）。入队可以发生在工作单元之内；Flush 发生在成功提交之后。
    /// handler 失败不能回滚已经提交的主写入 —— 需要与写库原子的跨进程消息请用发件箱。
    /// 作用域异常退出或业务失败则整队丢弃，不会出现「库回滚了但事件已经出去」。
    ///
    /// 事件类型约束：必须继承 <see cref="LocalEvent"/>，约束在编译期生效（<c>PublishAsync(new object())</c> 编译不过）。
    /// 与 NanaEvent / NanaLocalEvent 各走各的，不可互相继承。
    ///
    /// 触发点：HTTP（过滤器 / 中间件）、消息消费（<c>VivConsumer</c> / <c>VivLocalConsumer</c> 的
    /// <c>HandleAsync</c>）、以及非 MVC 宿主通过 <see cref="IVivLocalEventScope.RunAsync"/> 显式包一层。
    /// TickerQ / 手写 BackgroundService 没有自动触发点，请用 <see cref="IVivLocalEventScope"/>；
    /// 不包的话作用域结束只记一条「未分发」Warning，事件不会被分发。
    /// </summary>
    public interface IVivLocalEventBus
    {
        /// <summary>
        /// 发布本地事件 —— 只入队，不立即执行。
        /// 入参为 null 抛 <see cref="ArgumentNullException"/>。
        /// 可在工作单元之内调用；真正执行要等到 Flush（成功提交之后）。
        /// </summary>
        /// <typeparam name="TEvent">事件类型，须继承 <see cref="LocalEvent"/></typeparam>
        /// <param name="event">事件实例</param>
        /// <param name="ct">取消令牌</param>
        Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : LocalEvent;

        /// <summary>
        /// 分发全部待发事件，按入队顺序执行各事件的处理器。幂等，重复调用只有第一次生效。
        /// 框架调用（HTTP 过滤器 / 兜底中间件 / 消费者 HandleAsync / <see cref="IVivLocalEventScope"/>），
        /// 业务代码不要直接调用。
        /// 触发点一律传 <see cref="CancellationToken.None"/> —— 处理器必须跑完，不因客户端断开而跳过。
        /// 处理器抛出的异常直接上抛，不吞；Flush 失败时剩余事件丢弃，且不能回滚已经提交的主写入。
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
