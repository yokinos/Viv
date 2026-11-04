using Viv.Clockwork;
using Viv.Contracts.Interface;

namespace Viv.Coser.Api.Core
{
    /// <summary>一页数据：本页条目和总页数</summary>
    public sealed record PagedItems<T>(IReadOnlyList<T> Items, int TotalPages, long TotalCount);

    /// <summary>
    /// 定时任务的逐条处理基类。按页拉数据，逐条交给 <see cref="ProcessItemAsync"/>；
    /// 数据从哪来由子类决定，这里只负责翻页和快照校验。
    /// </summary>
    public abstract class CoserForeachBase<T> : VivTickerJobBase
    {
        protected CoserForeachBase(IVivContext context) : base(context)
        {
        }

        /// <summary>处理单条。抛异常就中断整个任务</summary>
        public abstract Task ProcessItemAsync(T item);

        /// <summary>从 pageIndex 开始翻页，逐条处理。开工前走基类的快照校验</summary>
        protected virtual Task ForeachAsync(
            int pageIndex,
            int pageSize,
            Func<int, int, CancellationToken, Task<PagedItems<T>>> getPage,
            CancellationToken cancellationToken = default)
            => RunAsync(async () =>
            {
                var index = Math.Max(1, pageIndex);
                var size = Math.Max(1, pageSize);

                while (true)
                {
                    var page = await getPage(index, size, cancellationToken).ConfigureAwait(false);

                    foreach (var item in page.Items)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await ProcessItemAsync(item).ConfigureAwait(false);
                    }

                    // TotalPages 为 0 表示没有数据，直接用当前页结束
                    if (index >= page.TotalPages) return;
                    index++;
                }
            }, cancellationToken);
    }
}
