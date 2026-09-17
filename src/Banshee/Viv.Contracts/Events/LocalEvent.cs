namespace Viv.Contracts.Events
{
    /// <summary>
    /// 本地事件基类，空标记。约束泛型参数，让 <c>PublishAsync(new object())</c> 编译不过 ——
    /// 本地事件的 handler 与发布方同作用域、是业务的一部分，事件类型不能随手拿个 DTO 充数。
    ///
    /// 三个事件根各走各的：跨进程继承 <c>NanaEvent</c> 走 IVivEventPublisher，
    /// 进程内异步点对点继承 <c>NanaLocalEvent</c> 走 IVivLocalEventPublisher，本类走 IVivLocalEventBus。
    ///
    /// 本类不可继承 <c>NanaEvent</c>：Wolverine 会给每个 NanaEvent 子类注册 RabbitMQ 路由。
    /// </summary>
    public abstract class LocalEvent
    {
    }
}
