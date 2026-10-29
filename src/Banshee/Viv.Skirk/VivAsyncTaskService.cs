using Viv.Delusion.Generic;
using System.Text.Json;

using Viv.Contracts.Interface;
using Viv.Delusion.Magic;
using Viv.Log;
using Viv.Skirk.Core;
using Viv.Skirk.Options;

namespace Viv.Skirk
{
    /// <summary>
    /// <see cref="IVivAsyncTask"/> 的默认实现：提交 / 查询 / 取消。
    ///
    /// 提交必须发生在业务事务内 —— 走 <c>IMomoDbContext.ExecuteSqlAsync</c>，
    /// 它认得当前事务（与业务行同一个 BEGIN/COMMIT）。
    /// </summary>
    public sealed class VivAsyncTaskService : IVivAsyncTask
    {
        /// <summary>表只建一次，进程内用信号量夹住</summary>
        private static readonly SemaphoreSlim TableGate = new(1, 1);

        private static bool _tableReady;

        private readonly VivAsyncTaskStore _store;
        private readonly ILoggerContract _logger;
        private readonly SkirkOption _option;
        private readonly IVivContext _context;

        public VivAsyncTaskService(
            VivAsyncTaskStore store,
            ILoggerContract logger,
            SkirkOption option,
            IVivContext context)
        {
            _store = store;
            _logger = logger;
            _option = option;
            _context = context;
        }

        /// <summary>参数 JSON 选项：入队与执行两侧必须一致</summary>
        internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        /// <inheritdoc />
        public async Task<long> EnqueueAsync<TParams>(
            string taskType,
            TParams @params,
            string? idempotencyKey = null,
            CancellationToken cancellationToken = default)
            where TParams : class
        {
            if (string.IsNullOrWhiteSpace(taskType))
            {
                throw new ArgumentException("任务类型不能为空", nameof(taskType));
            }

            await EnsureTableAsync(cancellationToken).ConfigureAwait(false);

            var now = DateTime.Now;
            var task = new VivAsyncTask
            {
                Id = IdMagic.NextId(),
                SubjectId = _context.SubjectId > 0 ? _context.SubjectId : null,
                AppId = _context.AppId > 0 ? _context.AppId : null,
                UserId = _context.UserId > 0 ? _context.UserId : null,
                TaskType = taskType,
                // 给什么存什么：参数结构的兼容由业务 handler 自己负责，框架不掺和
                ParamsJson = JsonSerializer.Serialize(@params, JsonOptions),
                Status = VivAsyncTaskStatus.Pending,
                Progress = 0,
                Attempt = 0,
                MaxAttempts = Math.Max(1, _option.MaxAttempts),
                NextAttemptAt = now,
                CancelRequested = false,
                IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey,
                TraceId = string.IsNullOrWhiteSpace(_context.TraceId) ? null : _context.TraceId,
                CreatedAt = now,
            };

            try
            {
                await _store.InsertAsync(task, cancellationToken).ConfigureAwait(false);
                return task.Id;
            }
            catch (Exception) when (task.IdempotencyKey is not null)
            {
                // 并发/重放：唯一索引冲突说明别人已经建过了，返回那条的 Id（幂等语义）
                var existingId = await _store.FindIdByIdempotencyKeyAsync(task.IdempotencyKey, cancellationToken).ConfigureAwait(false);
                if (existingId is not null) return existingId.Value;

                throw;
            }
        }

        /// <inheritdoc />
        public Task<VivAsyncTask?> GetAsync(long id, CancellationToken cancellationToken = default)
            => _store.GetAsync(id, cancellationToken);

        /// <inheritdoc />
        public async Task<PagedList<VivAsyncTask>> QueryAsync(VivAsyncTaskQuery query, CancellationToken cancellationToken = default)
            => await _store.QueryAsync(query, cancellationToken).ConfigureAwait(false);

        /// <inheritdoc />
        public async Task<bool> CancelAsync(long id, CancellationToken cancellationToken = default)
        {
            var affected = await _store.RequestCancelAsync(id, cancellationToken).ConfigureAwait(false);
            if (!affected)
            {
                _logger.Warning("取消任务失败（已到终态或不存在）：{0}", id);
            }

            return affected;
        }

        /// <summary>首次使用前确保表存在。建表失败直接抛 —— 后续入队本来也一定失败。</summary>
        private async Task EnsureTableAsync(CancellationToken cancellationToken)
        {
            if (_tableReady || !_option.AutoCreateTable) return;

            await TableGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_tableReady) return;

                await _store.EnsureTableAsync(cancellationToken).ConfigureAwait(false);
                _tableReady = true;
            }
            finally
            {
                TableGate.Release();
            }
        }
    }
}
