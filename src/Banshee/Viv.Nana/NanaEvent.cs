namespace Viv.Nana
{
    [Serializable]
    public abstract class NanaEvent
    {
        /// <summary>
        /// 消息优先级（业务载荷，框架不读）。
        /// </summary>
        public byte Priority { get; set; }

        /// <summary>
        /// 是否由定时任务作业发出（业务载荷，框架不读）。
        /// </summary>
        public bool IsJob { get; set; }
    }
}
