namespace Viv.Nana.Core
{
    [Serializable]
    public abstract class NanaEvent
    {
        /// <summary>
        /// 消息优先级
        /// </summary>
        public byte Priority { get; set; }

        /// <summary>
        /// 登录人的UserId
        /// </summary>
        public long UserId { get; set; }

        /// <summary>
        /// 是否由定时任务作业发出
        /// </summary>
        public bool IsJob { get; set; }
    }
}
