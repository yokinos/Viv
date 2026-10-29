namespace Viv.Skirk.Core
{
    /// <summary>
    /// 异步任务（表 <c>VivAsyncTask</c>）。
    ///
    /// 刻意**不是** EF 实体：这一层手写 SQL、自己建表，和 Outbox/Inbox 同一套做法，
    /// 好处是不参与实体扫描、不受 EF 模型缓存影响，模块完全独立。
    /// </summary>
    public sealed class VivAsyncTask
    {
        /// <summary>主键，由 <c>IdMagic.NextId()</c> 生成（雪花，long）</summary>
        public long Id { get; set; }

        /// <summary>客户端应用 Id（AtClientApp 多 App 体系）；后台任务按它还原上下文</summary>

        public long? AppId { get; set; }


        public long? SubjectId { get; set; }

        public long? UserId { get; set; }

        /// <summary>业务任务类型（如 export-users），与 <see cref="VivAsyncTaskHandler{TParams}.TaskType"/> 对应</summary>
        public string TaskType { get; set; } = string.Empty;

        /// <summary>参数 JSON（入队时原样序列化，不额外包版本）</summary>
        public string ParamsJson { get; set; } = "{}";

        public VivAsyncTaskStatus Status { get; set; }

        /// <summary>进度 0~100</summary>
        public int Progress { get; set; }

        /// <summary>
        /// 总数（如"865 个学员"）。为 null 表示总数未知 —— 前端据此决定要不要显示 "500/865"。
        /// </summary>
        public int? TotalCount { get; set; }

        /// <summary>已完成数（如 500）。与 <see cref="TotalCount"/> 配对使用。</summary>
        public int? DoneCount { get; set; }

        /// <summary>进度文案（"正在修改学籍"）。前端可作 tooltip 或状态说明。</summary>
        public string? ProgressMessage { get; set; }

        /// <summary>结果 JSON（只放小结果；大结果写对象存储存引用）</summary>
        public string? ResultJson { get; set; }

        public string? ErrorMessage { get; set; }

        /// <summary>已执行次数（含首次）</summary>
        public int Attempt { get; set; }

        public int MaxAttempts { get; set; } = 3;

        /// <summary>租约到期时间；为 null 表示没被认领</summary>
        public DateTime? LeaseUntil { get; set; }

        /// <summary>租约持有者（副本标识），续租与完成时用作乐观条件</summary>
        public string? LeaseOwner { get; set; }

        /// <summary>最早可执行时间（退避重试用）</summary>
        public DateTime NextAttemptAt { get; set; }

        /// <summary>用户请求取消；执行方协作式检查</summary>
        public bool CancelRequested { get; set; }

        /// <summary>提交幂等键，唯一索引（过滤空值），防止双击/重放产生重复任务</summary>
        public string? IdempotencyKey { get; set; }

        /// <summary>提交时的请求轨迹号，仅用于日志串联（**不是**主键）</summary>
        public string? TraceId { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? StartedAt { get; set; }

        public DateTime? FinishedAt { get; set; }

        /// <summary>保留到期时间，清理循环按它删</summary>
        public DateTime? ExpireAt { get; set; }
    }
}
