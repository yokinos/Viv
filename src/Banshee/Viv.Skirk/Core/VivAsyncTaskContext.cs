using Viv.Log;
using Viv.Skirk.Options;

namespace Viv.Skirk.Core
{
    /// <summary>
    /// 单个任务的执行上下文。
    ///
    /// 取消标记由 Worker 的心跳循环轮询后写进 <see cref="CanceledByUser"/>，
    /// 所以 <see cref="ThrowIfCancelRequested"/> 是纯内存判断，handler 可以在热循环里放心调用。
    /// </summary>
    internal sealed class VivAsyncTaskContext : IVivAsyncTaskContext
    {
        private readonly IVivAsyncTaskStore _store;
        private readonly SkirkOption _option;
        private readonly ILoggerContract _logger;

        private DateTime _lastReportAt = DateTime.MinValue;

        private volatile bool _canceledByUser;

        public VivAsyncTaskContext(long taskId, IVivAsyncTaskStore store, SkirkOption option, ILoggerContract logger)
        {
            TaskId = taskId;
            _store = store;
            _option = option;
            _logger = logger;
        }

        public long TaskId { get; }

        /// <summary>用户点了取消（由心跳循环更新）</summary>
        public bool CanceledByUser => _canceledByUser;

        /// <summary>心跳循环调用：更新取消标记</summary>
        public void SetCanceledByUser(bool canceled) => _canceledByUser = canceled;

        /// <inheritdoc />
        public Task ReportAsync(int percent, string? message = null, CancellationToken cancellationToken = default)
            => ReportCoreAsync(Math.Clamp(percent, 0, 100), null, null, message, cancellationToken);

        /// <inheritdoc />
        public Task ReportAsync(int doneCount, int totalCount, string? message = null, CancellationToken cancellationToken = default)
        {
            // 知道总数时百分比由框架算，handler 不用自己除
            var percent = totalCount <= 0 ? 0 : (int)Math.Round(doneCount * 100d / totalCount, MidpointRounding.AwayFromZero);

            return ReportCoreAsync(
                Math.Clamp(percent, 0, 100),
                Math.Max(0, totalCount),
                Math.Clamp(doneCount, 0, Math.Max(0, totalCount)),
                message,
                cancellationToken);
        }

        private async Task ReportCoreAsync(
            int percent,
            int? totalCount,
            int? doneCount,
            string? message,
            CancellationToken cancellationToken)
        {
            // 节流：进度写库比业务本身更容易把库打满
            var now = DateTime.UtcNow;
            if ((now - _lastReportAt).TotalMilliseconds < Math.Max(0, _option.ProgressMinIntervalMs)) return;
            _lastReportAt = now;

            try
            {
                await _store.ReportProgressAsync(TaskId, percent, totalCount, doneCount, Trim(message), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 进度是锦上添花，写失败不能拖垮任务
                _logger.Warning("Skirk 任务 {0} 进度上报失败：{1}", TaskId, ex.Message);
            }
        }

        /// <inheritdoc />
        public void ThrowIfCancelRequested()
        {
            if (_canceledByUser)
            {
                throw new OperationCanceledException($"任务 {TaskId} 已由用户取消");
            }
        }

        private static string? Trim(string? message)
            => message is null ? null : (message.Length <= 200 ? message : message[..200]);
    }
}
