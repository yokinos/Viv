using System.Threading;
using System.Threading.Tasks;
using Viv.Contracts.Interface;

namespace Viv.Contracts.Events
{
    /// <summary>
    /// 本地事件处理器基类 —— 与 <c>VivConsumer&lt;T&gt;</c> 同风格的写法，
    /// 但**完全独立于 Nana**，不引用其任何类型。
    ///
    /// <code>
    /// public class OrderCreatedHandler : LocalEventHandler&lt;OrderCreatedEvent&gt;
    /// {
    ///     private readonly IMomoDbContext _db;
    ///     public OrderCreatedHandler(IMomoDbContext db) =&gt; _db = db;
    ///
    ///     public override async Task HandleAsync(OrderCreatedEvent e, CancellationToken ct)
    ///     {
    ///         // 这里的 _db 与发布方是同一个实例、同一个作用域
    ///     }
    /// }
    /// </code>
    ///
    /// 【一个类只能订阅一个事件】
    /// C# 单继承所限，继承本基类后无法再订阅其他事件。
    /// 需要订阅多个事件时请直接实现 <see cref="IVivLocalEventHandler{TEvent}"/>。
    ///
    /// 【基类为何不提供 Logger / Context 属性】
    /// Viv.Contracts 只引用 Viv.Delusion，未引用 Viv.Log，带 ILoggerContract 就得为契约层加包引用。
    /// 处理器需要什么直接构造注入即可 —— 反正它本来就要注入 IMomoDbContext。
    ///
    /// 【注册方式】
    /// 无需任何特性，启动时按接口自动扫描注册（见 <see cref="IVivLocalEventHandler{TEvent}"/>）。
    /// </summary>
    /// <typeparam name="TEvent">事件类型，须继承 <see cref="EngineEvent"/></typeparam>
    public abstract class LocalEventHandler<TEvent> : IVivLocalEventHandler<TEvent>
        where TEvent : EngineEvent
    {
        /// <inheritdoc />
        public abstract Task HandleAsync(TEvent @event, CancellationToken ct = default);
    }
}
