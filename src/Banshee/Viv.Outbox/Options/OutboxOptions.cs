namespace Viv.Outbox.Options
{
    /// <summary>
    /// 发件箱配置。绑定自 appsettings.json 的 <c>VivOptions.OutboxOption</c> 节点。
    /// 为 null = 不启用（不注册投递器、不建表），与其它子系统同姿态。
    /// </summary>
    public class OutboxOptions
    {
        /// <summary>
        /// 总开关。与 <see cref="EnableDispatcher"/> 正交：那个决定投不投，这个决定装不装。
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 本进程是否运行投递器。默认开。
        /// 只在「只写 outbox、由别的服务负责投递」的部署里关掉。
        /// </summary>
        public bool EnableDispatcher { get; set; } = true;

        /// <summary>
        /// 兜底轮询间隔（秒）。
        ///
        /// 它决定的已经不是"平均投递延迟"了：本进程入队并提交后，投递器会被提交观察者立刻唤醒，
        /// 延迟是毫秒级。这个值只剩两个职责 —— 接住<b>别的进程</b>写进来的消息（进程内信号跨不过去），
        /// 接住信号丢失（通知方回滚、进程刚起）。所以它该按"故障恢复的容忍度"来定，默认 30 秒；
        /// 按"用户体验"去定成 1 秒是在为一个已经不走这条路的场景付轮询成本。
        ///
        /// ★ 例外：投递器与写库不在同一进程的部署（例如 API 写入、Worker 投递），
        /// 进程内信号打不过去，这个值会重新变回"真实延迟上界"，那边必须单独配小。
        /// </summary>
        public int PollIntervalSeconds { get; set; } = 30;

        /// <summary>单批认领行数。</summary>
        public int BatchSize { get; set; } = 100;

        /// <summary>单条消息最大重试次数，超过置为 <c>Failed</c> 等待人工介入（不静默丢）。</summary>
        public int MaxRetryCount { get; set; } = 10;

        /// <summary>
        /// 认领租约时长（秒）。投递器崩溃 / 被杀后，超过此时长仍处于 Processing 的行会被重新认领。
        /// 它同时是「同一条消息被投递两次」的窗口来源（at-least-once 的代价）。
        /// </summary>
        public int LeaseSeconds { get; set; } = 60;

        /// <summary>启动时自动建表（DDL 幂等，多实例并发启动安全）。</summary>
        public bool AutoCreateTable { get; set; } = true;

        /// <summary>已发送行的保留天数，超期分批清理。0 或负数 = 不清理。</summary>
        public int RetentionDays { get; set; } = 7;
    }
}
