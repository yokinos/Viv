using Viv.Delusion.Generic;

using Viv.Skirk.Core;

namespace Viv.Skirk
{
    /// <summary>
    /// 异步任务端口：提交 / 查询 / 取消。
    ///
    /// 提交必须发生在**业务事务内**（与业务行一起提交），否则会出现「业务回滚了、任务还在跑」的幽灵任务。
    /// </summary>
    public interface IVivAsyncTask
    {
        /// <summary>
        /// 提交一个任务，返回任务 Id。
        ///
        /// <paramref name="idempotencyKey"/> 给定时命中已有任务直接返回它的 Id（不会重复建）；
        /// 建议格式 <c>{userId}:{taskType}:{业务键}</c>。
        /// </summary>
        Task<long> EnqueueAsync<TParams>(
            string taskType,
            TParams @params,
            string? idempotencyKey = null,
            CancellationToken cancellationToken = default)
            where TParams : class;

        /// <summary>按 Id 取任务（用户查进度用；找不到返回 null）</summary>
        Task<VivAsyncTask?> GetAsync(long id, CancellationToken cancellationToken = default);

        /// <summary>按条件查列表（"我的任务"）</summary>
        Task<PagedList<VivAsyncTask>> QueryAsync(VivAsyncTaskQuery query, CancellationToken cancellationToken = default);

        /// <summary>
        /// 请求取消。只置标记，执行方协作式退出；返回 false 表示任务已到终态、无需取消。
        /// </summary>
        Task<bool> CancelAsync(long id, CancellationToken cancellationToken = default);
    }

    /// <summary>列表查询条件，字段为空表示不限制。</summary>
    public sealed class VivAsyncTaskQuery
    {
        public long? SubjectId { get; set; }

        public long? UserId { get; set; }

        public string? TaskType { get; set; }

        public VivAsyncTaskStatus? Status { get; set; }

        /// <summary>最多返回条数，默认 50，最大 200</summary>
        public int Limit { get; set; } = 50;

        public int PageIndex { get; set; } = 1;

        public int PageSize { get; set; } = 20;
    }
}
