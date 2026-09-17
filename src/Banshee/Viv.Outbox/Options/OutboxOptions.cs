namespace Viv.Outbox.Options
{
    /// <summary>
    /// 发件箱配置。绑定自 appsettings.json 的 <c>VivOptions.OutboxOption</c> 节点。
    /// <b>为 null = 不启用</b>（不注册投递器、不建表），与其它子系统同姿态。
    /// </summary>
    public class OutboxOptions
    {
        /// <summary>
        /// 本进程是否运行投递器。默认开。
        /// 只在「只写 outbox、由别的服务负责投递」的部署里关掉。
        /// </summary>
        public bool EnableDispatcher { get; set; } = true;

        /// <summary>轮询间隔（秒）—— 决定投递延迟的上界。</summary>
        public int PollIntervalSeconds { get; set; } = 5;

        /// <summary>单批认领行数。</summary>
        public int BatchSize { get; set; } = 100;

        /// <summary>单条消息最大重试次数，超过置为 <c>Failed</c> 等待人工介入（不静默丢）。</summary>
        public int MaxRetryCount { get; set; } = 10;

        /// <summary>
        /// 认领租约时长（秒）。投递器崩溃 / 被杀后，超过此时长仍处于 Processing 的行会被重新认领。
        /// <b>它同时是「同一条消息被投递两次」的窗口来源</b>（at-least-once 的代价）。
        /// </summary>
        public int LeaseSeconds { get; set; } = 60;

        /// <summary>启动时自动建表（DDL 幂等，多实例并发启动安全）。</summary>
        public bool AutoCreateTable { get; set; } = true;

        /// <summary>已发送行的保留天数，超期分批清理。0 或负数 = 不清理。</summary>
        public int RetentionDays { get; set; } = 7;
    }
}
