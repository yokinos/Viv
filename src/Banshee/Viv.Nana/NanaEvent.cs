namespace Viv.Nana
{
    /// <summary>
    /// 跨进程事件基类（走 RabbitMQ，消费端是 VivConsumer&lt;T&gt;）。
    /// </summary>
    [Serializable]
    public abstract class NanaEvent
    {
        /// <summary>
        /// 是否由定时任务作业发出（业务载荷，框架不读）。
        /// </summary>
        public bool IsJob { get; set; }
    }
}
