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

        /// <summary>
        /// 消息消费失败允许重试次数 这个次数归零 就不再重试（入库等待人工干预）
        /// </summary>
        public int TryCount { get; set; } = 5;
    }
}
