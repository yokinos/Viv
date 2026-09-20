using System.Threading;
using System.Threading.Tasks;
using Viv.Contracts.Events;

namespace Viv.Contracts.Interface
{
    /// <summary>
    /// 本地事件处理器契约 —— 扫描、注册、分发的目标类型。
    /// <list type="bullet">
    /// <item><description>接口与基类：基类 <see cref="LocalEventHandler{TEvent}"/> 提供与 VivConsumer&lt;T&gt; 一致的编码手感；C#单继承限制，基于基类的处理器仅能订阅单个事件，如需订阅多个事件，直接实现本接口。基类已实现本接口，两种写法均可被扫描识别，注册逻辑统一。</description></item>
    /// <item><description>自动注册：实现类无需添加任何特性，也不用实现 IDependency；LocalEventRegistration 启动时扫描该开放泛型接口，自动以 Scoped 完成注册。</description></item>
    /// <item><description>执行语义：与发布方共用同一个DI作用域；同一事件多个处理器按解析顺序依次执行；处理器抛出异常会中断后续处理器并向上冒泡，本地事件属于主业务流程，不会静默吞异常；处理器内部可再次 PublishAsync，新事件进下一轮分发（单次 Flush 最多 5 轮，超限记错误日志并丢弃）。</description></item>
    /// </list>
    /// </summary>
    /// <typeparam name="TEvent">事件类型，须继承 <see cref="LocalEvent"/></typeparam>
    public interface IVivLocalEventHandler<in TEvent> where TEvent : LocalEvent
    {
        /// <summary>
        /// 处理事件。实现体即业务逻辑。
        ///
        /// 本方法是业务的一部分，必须执行完 —— 框架触发点一律传 <see cref="CancellationToken.None"/>，
        /// 不会因客户端断开而放它鸽子。若处理逻辑「不执行也无所谓」，它就不该是本地事件，应该走 MQ。
        /// </summary>
        /// <param name="event">事件实例</param>
        /// <param name="ct">取消令牌</param>
        Task HandleAsync(TEvent @event, CancellationToken ct = default);
    }
}
