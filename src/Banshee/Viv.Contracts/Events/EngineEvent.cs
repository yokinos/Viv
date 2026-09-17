namespace Viv.Contracts.Events
{
    /// <summary>
    /// 本地事件基类 —— 空标记，只为「纯限制」而存在。
    ///
    /// 【为什么要有它】
    /// 本地事件的处理器是**业务的一部分**（与发布方同作用域、同数据上下文），写下来就必须执行。
    /// 所以事件类型必须是有意识声明的业务事件，不能随手丢个 object / 临时 DTO 进来充数。
    /// 泛型约束把这层要求前移到编译期：<c>PublishAsync(new object())</c> 直接编译不过。
    ///
    /// 【与 NanaEvent 是二选一的两条路，互不继承】
    /// - 要跨进程 → 继承 <c>NanaEvent</c>，走 IVivEventPublisher（RabbitMQ）
    /// - 只在进程内 → 继承本类，走 IVivLocalEventBus
    ///
    /// ⚠️ <b>绝不可让本类去继承 NanaEvent</b> —— Wolverine 对每个 NanaEvent 子类都注册了
    /// RabbitMQ 路由（VivWolverineConfigurationExtensions），继承即把所有本地事件绑死成跨进程语义。
    /// </summary>
    public abstract class EngineEvent
    {
    }
}
