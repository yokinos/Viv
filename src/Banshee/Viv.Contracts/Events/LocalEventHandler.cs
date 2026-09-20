using System.Threading;
using System.Threading.Tasks;
using Viv.Contracts.Interface;

namespace Viv.Contracts.Events
{
    /// <summary>
    /// 本地事件处理器基类 —— 与 <c>VivConsumer&lt;T&gt;</c> 同风格的写法，但完全独立于 Nana，不引用其任何类型。
    ///
    /// <code>
    /// public class OrderCreatedHandler : LocalEventHandler&lt;OrderCreatedEvent&gt;
    /// {
    ///     private readonly IMomoDbContext _db;
    ///     public OrderCreatedHandler(IMomoDbContext db) =&gt; _db = db;
    ///
    ///     public override async Task HandleAsync(OrderCreatedEvent e, CancellationToken ct)
    ///     {
    ///         // 这里的 _db 与发布方是同一个实例、同一个作用域，
    ///         // 但本方法跑在主业务提交之后，失败不能回滚已经落库的写。
    ///     }
    /// }
    /// </code>
    ///
    /// 一个类只能订阅一个事件（C# 单继承），要订阅多个就直接实现 <see cref="IVivLocalEventHandler{TEvent}"/>。
    ///
    /// 基类不提供 Logger / Context 属性：Viv.Contracts 只引用 Viv.Delusion，未引用 Viv.Log，
    /// 带上 ILoggerContract 就得给契约层加包引用。处理器需要什么直接构造注入即可。
    ///
    /// 无需任何特性，启动时按接口自动扫描注册。
    /// </summary>
    /// <typeparam name="TEvent">事件类型，须继承 <see cref="LocalEvent"/></typeparam>
    public abstract class LocalEventHandler<TEvent> : IVivLocalEventHandler<TEvent>
        where TEvent : LocalEvent
    {
        /// <inheritdoc />
        public abstract Task HandleAsync(TEvent @event, CancellationToken ct = default);
    }
}
