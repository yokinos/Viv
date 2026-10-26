namespace Viv.Contracts.Interface
{
    /// <summary>
    /// 事务尝试观察者。
    ///
    /// 开启自动重试后，一个事务可能被**整段重放多次**。凡是「按尝试累积、失败要撤销」的外部状态
    /// （本地事件队列、内存计数、缓存暂存……）都实现本接口，由事务实现分别在
    /// 「一次尝试开始」和「该次尝试失败」时回调。
    ///
    /// 刻意不让事务层认识具体是哪种状态 —— 它只知道"有东西需要按尝试回退"。
    /// </summary>
    public interface ITransactionAttemptListener
    {
        /// <summary>
        /// 一次尝试开始（首次或重试）。此刻记下当前的可回退位置。
        /// </summary>
        void OnAttemptStarting();

        /// <summary>
        /// 本次尝试失败、即将回滚。撤销这次尝试产生的内存副作用。
        /// 在回滚之前调用；实现不应抛异常（抛了会盖住真正的失败原因）。
        /// </summary>
        void OnAttemptFailed();
    }
}
