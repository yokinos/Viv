namespace Viv.Outbox.Options
{
    /// <summary>
    /// Inbox 清理配置。绑定自 appsettings.json 的 <c>VivOptions.InboxOption</c> 节点。
    ///
    /// 与其它子系统不同，为 null 不是「不启用」—— Inbox 的注册只要求配了 DatabaseOption（消费者想用就能用），
    /// 所以节点缺席时这里取默认值，让清理照常运行。要关掉清理把 <see cref="RetentionDays"/> 配成 0 或负数。
    /// </summary>
    public class InboxOptions
    {
        /// <summary>
        /// 已接受行的保留天数，超期分批清理。0 或负数 = 不清理。
        ///
        /// 它同时是去重窗口：行被删掉之后，同一条消息再被投递就会被当成新消息重新处理。
        /// 所以保留期要长于「同一条消息最晚可能被重投」的时间窗 —— 要把消费端退避重试、
        /// RedeliverAsync 的递增延迟、发件箱自身的重试与租约过期重投叠起来算。
        /// 人工把 Failed 的发件箱行捞回来重投属于框架管不到的路径，那种情况本来就不是自动幂等能覆盖的。
        /// </summary>
        public int RetentionDays { get; set; } = 7;

        /// <summary>单批删除行数。一批删不动就换下一轮，不在一轮里无限删。</summary>
        public int BatchSize { get; set; } = 1000;

        /// <summary>
        /// 清理轮询间隔（分钟）。Inbox 是慢增长的幂等表，不像发件箱那样要求低延迟，
        /// 所以间隔用分钟级 —— 没必要每几秒去扫一次。
        /// </summary>
        public int CleanupIntervalMinutes { get; set; } = 60;
    }
}
