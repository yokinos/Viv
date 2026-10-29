namespace Viv.Skirk
{
    /// <summary>
    /// 任务执行上下文，由 Worker 在每次执行时构造。
    /// </summary>
    public interface IVivAsyncTaskContext
    {
        long TaskId { get; }

        /// <summary>
        /// 上报进度（0~100）。内部按 <c>ProgressMinIntervalMs</c> 节流，写库失败只记日志、不影响任务。
        /// </summary>
        Task ReportAsync(int percent, string? message = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 按"已完成/总数"上报（如 500/865）。百分比由框架计算，同时记录两个计数 ——
        /// 前端据此决定要不要在 tooltip 里显示 "500/865"；只传百分比则两个计数留空。
        /// </summary>
        Task ReportAsync(int doneCount, int totalCount, string? message = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 取消检查。用户点了取消时抛 <see cref="OperationCanceledException"/>，
        /// 长循环里要定期调用（如每批处理完）。
        /// </summary>
        void ThrowIfCancelRequested();
    }

    /// <summary>
    /// 任务处理器（非泛型面）：Worker 只认这个接口，参数以 object 传入。
    /// </summary>
    public interface IVivAsyncTaskHandler
    {
        /// <summary>
        /// 业务任务类型，与入队时的 type 一致
        /// </summary>
        string TaskType { get; }

        /// <summary>
        /// 参数类型，用于把 ParamsJson 反序列化回来
        /// </summary>
        Type ParamsType { get; }

        Task ExecuteAsync(object? @params, IVivAsyncTaskContext context, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 业务只继承这个类写强类型逻辑，强转只发生在基类内部一次。
    /// </summary>
    public abstract class VivAsyncTaskHandler<TRequest> : IVivAsyncTaskHandler
        where TRequest : class
    {
        /// <inheritdoc />
        public abstract string TaskType { get; }

        /// <inheritdoc />
        public Type ParamsType => typeof(TRequest);

        /// <inheritdoc />
        public Task ExecuteAsync(object? request, IVivAsyncTaskContext context, CancellationToken cancellationToken)
            => ExecuteAsync((TRequest?)request, context, cancellationToken);

        /// <summary>业务实现。参数可能为 null（入队时没给参数）。</summary>
        protected abstract Task ExecuteAsync(TRequest? request, IVivAsyncTaskContext context, CancellationToken cancellationToken);
    }
}
