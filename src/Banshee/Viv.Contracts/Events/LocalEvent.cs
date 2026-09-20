namespace Viv.Contracts.Events
{
    /// <summary>
    /// 本地事件基类，空标记。约束泛型参数，让 <c>PublishAsync(new object())</c> 编译不过。
    ///
    /// 四个事件根各走各的：跨进程继承 <c>NanaEvent</c> 走 IVivEventPublisher
    /// （要与写库原子则换成 IVivOutbox，事件类型不变）；
    /// 进程内异步点对点继承 <c>NanaLocalEvent</c> 走 IVivLocalEventPublisher（与数据库事务无关）；
    /// 本类走 IVivLocalEventBus（入队可在工作单元内，Flush 在成功提交之后，handler 失败不能回滚主写入）。
    ///
    /// 本类不可继承 <c>NanaEvent</c>：Wolverine 会给每个 NanaEvent 子类注册 RabbitMQ 路由。
    /// </summary>
    public abstract class LocalEvent
    {
    }
}
