namespace Viv.Nana
{
    /// <summary>
    /// 跨进程事件基类（走 RabbitMQ，消费端是 VivConsumer&lt;T&gt;）。
    /// </summary>
    [Serializable]
    public abstract class NanaEvent
    {
        /// <summary>
        /// 投递来源：直接发出 / 发件箱投递器 / 定时任务作业
        /// </summary>
        public DeliverySource DeliverySource { get; set; } = DeliverySource.Direct;
    }
}