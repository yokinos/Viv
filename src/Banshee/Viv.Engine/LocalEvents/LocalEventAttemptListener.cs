using Viv.Contracts.Interface;

namespace Viv.Engine.LocalEvents
{
    /// <summary>
    /// 把「事务尝试」接到本地事件队列上：每次尝试记下队尾，尝试失败就回退到那里。
    ///
    /// 为什么需要：开启 EF 自动重试后，失败那次尝试入队的事件对应的是**已经回滚**的写入；
    /// 不撤掉的话，重试成功那次会把它们一起分发出去（幽灵事件）。
    ///
    /// Scoped —— 与本地事件总线同生命周期，重试是顺序发生的，一个 <see cref="_mark"/> 够用。
    /// </summary>
    internal sealed class LocalEventAttemptListener : ITransactionAttemptListener
    {
        private readonly IVivLocalEventBus _bus;

        private int _mark = -1;

        public LocalEventAttemptListener(IVivLocalEventBus bus)
        {
            _bus = bus;
        }

        public void OnAttemptStarting() => _mark = _bus.Mark();

        public void OnAttemptFailed()
        {
            if (_mark < 0) return;

            _bus.RewindTo(_mark);
        }
    }
}
