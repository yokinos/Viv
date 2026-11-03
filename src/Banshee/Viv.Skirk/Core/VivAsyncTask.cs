namespace Viv.Skirk.Core
{
    /// <summary>
    /// 异步任务（表 <c>VivAsyncTask</c>）。
    /// </summary>
    public sealed class VivAsyncTask
    {
        /// <summary>
        /// 主键，由 <c>IdMagic.NextId()</c> 生成（雪花，long）。
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// 客户端应用 Id 后台执行时据此还原上下文。
        /// </summary>
        public long? AppId { get; set; }

        /// <summary>
        /// 关联主体 Id，语义由业务定义。
        /// </summary>
        public long? SubjectId { get; set; }

        /// <summary>
        /// 关联用户 Id。
        /// </summary>
        public long? UserId { get; set; }

        /// <summary>
        /// 业务任务类型（如 <c>export-users</c>），与
        /// <see cref="VivAsyncTaskHandler{TParams}.TaskType"/> 对应，用于路由到处理器。
        /// </summary>
        public string TaskType { get; set; } = string.Empty;

        /// <summary>
        /// 参数 JSON，入队时原样序列化，不额外包版本。
        /// </summary>
        public string ParamsJson { get; set; } = "{}";

        /// <summary>
        /// 任务状态，取值见 <see cref="VivAsyncTaskStatus"/>。
        /// </summary>
        public VivAsyncTaskStatus Status { get; set; }

        /// <summary>
        /// 进度 0~100。
        /// </summary>
        public int Progress { get; set; }

        /// <summary>
        /// 总数（如 865 个学员）。为 <c>null</c> 表示总数未知，
        /// 前端据此决定是否显示 "500/865"。
        /// </summary>
        public int? TotalCount { get; set; }

        /// <summary>
        /// 已完成数（如 500），与 <see cref="TotalCount"/> 配对使用。
        /// </summary>
        public int? DoneCount { get; set; }

        /// <summary>
        /// 进度文案（如"正在修改学籍"），前端可用作 tooltip 或状态说明。
        /// </summary>
        public string? ProgressMessage { get; set; }

        /// <summary>
        /// 结果 JSON；只放小结果，大结果写入对象存储后在此存引用。
        /// </summary>
        public string? ResultJson { get; set; }

        /// <summary>
        /// 失败原因，供前端展示与排查。
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 已执行次数（含首次），与 <see cref="MaxAttempts"/> 比较判断是否耗尽重试。
        /// </summary>
        public int Attempt { get; set; }

        /// <summary>
        /// 最大执行次数。
        /// </summary>
        public int MaxAttempts { get; set; } = 3;

        /// <summary>
        /// 租约到期时间；为 <c>null</c> 表示尚未被认领。
        /// </summary>
        public DateTime? LeaseUntil { get; set; }

        /// <summary>
        /// 租约持有者（副本标识），续租与完成时用作乐观并发条件。
        /// </summary>
        public string? LeaseOwner { get; set; }

        /// <summary>
        /// 最早可执行时间，用于退避重试。
        /// </summary>
        public DateTime NextAttemptAt { get; set; }

        /// <summary>
        /// 用户请求取消；由执行方协作式检查。
        /// </summary>
        public bool CancelRequested { get; set; }

        /// <summary>
        /// 提交幂等键，唯一索引（过滤空值），防止双击/重放产生重复任务。
        /// </summary>
        public string? IdempotencyKey { get; set; }

        /// <summary>
        /// 提交时的请求轨迹号，仅用于日志串联，不是主键。
        /// </summary>
        public string? TraceId { get; set; }

        /// <summary>
        /// 创建时间（入队时刻）。
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 开始执行时间；首次认领时写入。
        /// </summary>
        public DateTime? StartedAt { get; set; }

        /// <summary>
        /// 结束时间（成功、失败或取消）。
        /// </summary>
        public DateTime? FinishedAt { get; set; }

        /// <summary>
        /// 保留到期时间，清理循环按此删除记录。
        /// </summary>
        public DateTime? ExpireAt { get; set; }
    }
}