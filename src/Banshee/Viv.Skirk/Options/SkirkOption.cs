namespace Viv.Skirk.Options
{
    /// <summary>
    /// 异步任务（Skirk）配置。绑在 <c>VivOptions.SkirkOption</c> 下。
    /// </summary>
    public sealed class SkirkOption
    {
        /// <summary>
        /// 本宿主是否跑任务消费者。
        ///
        /// <c>null</c> = 不启用（默认）。Api 与 Worker 共享同一个库，
        /// 谁来执行只看这一项：只有 Worker 宿主置 true，或者单体部署时 Api 也置 true。
        /// 两个宿主都置 true 也安全 —— 认领走数据库原子操作，不会重复执行。
        /// </summary>
        public bool EnableWorker { get; set; }

        /// <summary>
        /// 首次启动/首次入队时自动建表（幂等 DDL）。默认开，保持一行启动。
        /// 生产要由 DBA 建表时置 false。
        /// </summary>
        public bool AutoCreateTable { get; set; } = true;

        /// <summary>轮询间隔（秒）。越小延迟越低、库压力越大，默认 2。</summary>
        public int PollIntervalSeconds { get; set; } = 2;

        /// <summary>一次认领多少条。默认 4。</summary>
        public int BatchSize { get; set; } = 4;

        /// <summary>
        /// 单副本并发执行数。默认 4；在 Api 宿主里跑时建议调成 1~2，
        /// 否则批量任务会和接口请求抢线程池。
        /// </summary>
        public int MaxParallelism { get; set; } = 4;

        /// <summary>租约时长（秒）。执行期间按它的 1/2 续租；超时未续租的任务会被其他副本接管。默认 60。</summary>
        public int LeaseSeconds { get; set; } = 60;

        /// <summary>单个任务最大重试次数（不含首次）。默认 3。</summary>
        public int MaxAttempts { get; set; } = 3;

        /// <summary>单个任务执行超时（分钟）。默认 10。</summary>
        public int TaskTimeoutMinutes { get; set; } = 10;

        /// <summary>已完成/已取消任务的保留天数，到期由清理循环删除。默认 15。</summary>
        public int RetentionDays { get; set; } = 15;

        /// <summary>进度上报的最小间隔（毫秒）。默认 1000，防止进度写库打爆。</summary>
        public int ProgressMinIntervalMs { get; set; } = 1000;

        /// <summary>取消标记的轮询间隔（毫秒）。默认 500。</summary>
        public int CancelPollIntervalMs { get; set; } = 500;
    }
}
