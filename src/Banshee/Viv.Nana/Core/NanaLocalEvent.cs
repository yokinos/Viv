namespace Viv.Nana.Core
{
    /// <summary>
    /// 本地队列事件基类 —— 进程内异步消息，走 Wolverine 本地队列（不出网）。
    ///
    /// 【三种事件通道的取舍】按「跨不跨进程」和「同不同作用域」两个维度分野：
    /// - 继承 <see cref="NanaEvent"/> → <c>IVivEventPublisher</c>（RabbitMQ）：跨进程，fanout 广播，每服务各收一份
    /// - 继承本类               → <c>IVivLocalEventPublisher</c>（本地队列）：进程内、点对点、异步、独立 DI 作用域
    /// - 继承 <c>EngineEvent</c> → <c>IVivLocalEventBus</c>（本地总线）：进程内、fanout、同步、**同 DI 作用域**
    ///
    /// 本地队列的独特价值是「不阻塞调用方 + 不进另一个进程 + 自带延迟/重试/死信」：
    /// 本地总线做不到不阻塞（它是同步的，handler 必须跑完才返回），MQ 做不到不出网。
    ///
    /// 【点对点，不是广播】一个本地事件对应一条本地队列，Wolverine 对同一消息类型只认一条 handler chain，
    /// 即**一个本地事件只有一个消费者**。要「一个事件触发多个反应」，用本地总线（EngineEvent + LocalEventHandler，
    /// fanout 到全部 handler）。
    ///
    /// ⚠️【绝不继承 NanaEvent】
    /// 本类是 <see cref="NanaEvent"/> 的**平行根，不是子类**。一旦继承，VivWolverineConfigurationExtensions
    /// 里那段 <c>foreach (var eventType in TypeScanMagic.ScanTypes&lt;NanaEvent&gt;())</c> 会给它注册
    /// <c>ToRabbitExchange</c> 路由，于是每次发布**双发**（一条到 RabbitMQ、一条到本地队列），
    /// 而且编译期毫无提示。由 <c>NanaLocalEventTests.NanaLocalEvent是空标记基类_且与NanaEvent互不继承</c> 守住。
    ///
    /// 同理，本类与 <c>EngineEvent</c> 也是互不继承的两条路：本类要引 Wolverine（在 Viv.Nana），
    /// EngineEvent 零依赖（在 Viv.Contracts，业务 Core 直接写同步 handler 用）。
    /// </summary>
    [System.Serializable]
    public abstract class NanaLocalEvent
    {
    }
}
