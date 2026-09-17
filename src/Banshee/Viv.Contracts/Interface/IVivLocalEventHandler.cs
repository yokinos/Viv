using System.Threading;
using System.Threading.Tasks;
using Viv.Contracts.Events;

namespace Viv.Contracts.Interface
{
    /// <summary>
    /// 本地事件处理器契约 —— 扫描、注册、分发的目标类型。
    ///
    /// 【为什么既有接口又有基类】
    /// <see cref="LocalEventHandler{TEvent}"/> 基类提供与 VivConsumer&lt;T&gt; 一致的写法手感；
    /// 但 C# 单继承，只靠基类的话一个处理器永远只能订阅一个事件。
    /// 需要订阅多个事件时直接实现本接口。
    /// 基类实现了本接口，所以两种写法都能被扫描到，注册逻辑只有一份。
    ///
    /// 【注册方式】
    /// 实现类无需打任何特性，也无需实现 IDependency ——
    /// LocalEventRegistration 启动时扫描本接口的开放泛型（Viv.Contracts.Interface.IVivLocalEventHandler&lt;&gt;），
    /// 自动按 Scoped 注册进容器。
    ///
    /// 【执行语义】
    /// - 与发布方同一 DI 作用域，注入的 IMomoDbContext / IVivContext 就是发布方那一个
    /// - 同一事件有多个处理器时按解析顺序全部执行
    /// - 处理器抛异常会中断后续处理器并向上冒泡（本地事件是主业务流的一部分，不静默吞）
    /// - 处理器内可再次 PublishAsync，新事件在同一轮分发中被处理
    /// </summary>
    /// <typeparam name="TEvent">事件类型，须继承 <see cref="EngineEvent"/></typeparam>
    public interface IVivLocalEventHandler<in TEvent> where TEvent : EngineEvent
    {
        /// <summary>
        /// 处理事件。实现体即业务逻辑。
        ///
        /// <b>本方法是业务的一部分，必须执行完</b> —— 框架触发点一律传
        /// <see cref="CancellationToken.None"/>，不会因客户端断开而放它鸽子。
        /// 如果你的处理逻辑可以「不执行也无所谓」，它就不该是本地事件，应该走 MQ。
        /// </summary>
        /// <param name="event">事件实例</param>
        /// <param name="ct">取消令牌</param>
        Task HandleAsync(TEvent @event, CancellationToken ct = default);
    }
}
