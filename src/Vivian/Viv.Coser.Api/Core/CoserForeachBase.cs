using Viv.Clockwork;
using Viv.Contracts.Interface;

namespace Viv.Coser.Api.Core
{
    /// <summary>一页数据。消费方只需要这两样：本页条目与总页数</summary>
    public sealed record PagedItems<T>(IReadOnlyList<T> Items, int TotalPages, long TotalCount);

    /// <summary>
    /// 定时任务的"逐条处理"基类：按页拉数据，逐条交给 <see cref="ProcessItemAsync"/>。
    /// 翻页来源由子类给（应用、租户、订单……），这里只管遍历与快照校验。
    /// </summary>
    public abstract class CoserForeachBase<T> : VivTickerJobBase
    {
        protected CoserForeachBase(IVivContext context) : base(context)
        {
        }

        /// <summary>处理单条。抛异常即中断整个任务</summary>
        public abstract Task ProcessItemAsync(T item);

        /// <summary>
        /// 从 <paramref name="pageIndex"/> 开始翻页，逐条处理。
        /// 开工前校验租户/系统租户快照（继承自 <see cref="VivTickerJobBase"/>）。
        /// </summary>
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
