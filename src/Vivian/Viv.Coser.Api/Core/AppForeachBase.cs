using Viv.Contracts.Interface;
using Viv.Apex.Proxy.Ports;

namespace Viv.Coser.Api.Core
{
    /// <summary>
    /// 遍历客户端应用。数据在 Apex，Coser 没有自己的库，所以走 gRPC 分页查。
    /// </summary>
    public abstract class AppForeachBase : CoserForeachBase<AppItem>
    {
        private readonly IProxyApexAppService _apexApps;

        protected AppForeachBase(IVivContext context, IProxyApexAppService apexApps) : base(context)
        {
            _apexApps = apexApps ?? throw new ArgumentNullException(nameof(apexApps));
        }

        /// <summary>从第 1 页遍历全部应用，默认每页 100 条（服务端上限 200）</summary>
        public Task ForeachAllAsync(int pageSize = 100, CancellationToken cancellationToken = default)
            => ForeachAsync(
                pageIndex: 1,
                pageSize,
                async (index, size, token) =>
                {
                    var page = await _apexApps
                        .GetAppPageAsync(new AppPageRequest { PageIndex = index, PageSize = size }, token)
                        .ConfigureAwait(false);

                    return new PagedItems<AppItem>(page.Items, page.TotalPages, page.TotalCount);
                },
                cancellationToken);
    }
}
