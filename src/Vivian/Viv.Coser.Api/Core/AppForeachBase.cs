using Viv.Contracts.Interface;
using Viv.ServiceProxy.Apex.Ports;

namespace Viv.Coser.Api.Core
{
    /// <summary>
    /// 遍历所有客户端应用。数据在 Apex，Coser 没有自己的库，所以走 gRPC 分页查，
    /// 不把 AtClientApp 抄一份过来。
    /// </summary>
    public abstract class AppForeachBase : CoserForeachBase<AppItem>
    {
        private readonly IProxyApexAppService _clientAppQuery;

        protected AppForeachBase(IVivContext context, IProxyApexAppService clientAppQuery) : base(context)
        {
            _clientAppQuery = clientAppQuery ?? throw new ArgumentNullException(nameof(clientAppQuery));
        }

        /// <summary>从第 1 页开始遍历全部应用；默认每页 100 条（服务端上限 200）</summary>
        public Task ForeachAllAsync(int pageSize = 100,CancellationToken cancellationToken = default)=> ForeachAsync(
                pageIndex: 1,
                pageSize,
                async (index, size, token) =>
                {
                    var page = await _clientAppQuery.GetAppPageAsync(new AppPageRequest { PageIndex = index, PageSize = size }, token).ConfigureAwait(false);
                    return new PagedItems<AppItem>(page.Items, page.TotalPages, page.TotalCount);
                },
                cancellationToken);
    }
}
