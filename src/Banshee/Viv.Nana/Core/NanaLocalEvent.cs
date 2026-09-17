namespace Viv.Nana.Core
{
    /// <summary>
    /// 本地队列事件基类 —— 进程内异步消息，走 Wolverine 本地队列，不出网。
    ///
    /// 三条事件通道：继承 <see cref="NanaEvent"/> 走 RabbitMQ（跨进程 fanout）；
    /// 继承本类走本地队列（进程内点对点、异步、独立 DI 作用域）；继承 <c>LocalEvent</c> 走本地总线（进程内 fanout、同步、同作用域）。
    ///
    /// 点对点：一个本地事件只有一个消费者。要一个事件触发多个反应，用本地总线。
    ///
    /// 本类是 <see cref="NanaEvent"/> 的平行根，不是子类，绝不可继承它 ——
    /// Wolverine 给每个 NanaEvent 子类都注册了 ToRabbitExchange 路由，继承会让每次发布双发（MQ + 本地队列），且编译期毫无提示。
    /// </summary>
    [System.Serializable]
    public abstract class NanaLocalEvent
    {
    }
}
