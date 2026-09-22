using System.Collections.Generic;
using Viv.Momo.Interface;

namespace Viv.Momo.DataFilter
{
    /// <summary>
    /// 框架自带的读过滤器清单
    /// </summary>
    public static class MomoDataFilters
    {
        /// <summary>
        /// 全部读过滤器。加一条就在这里挂一个，这是除实现类之外唯一要动的地方。
        /// 顺序即 SQL 条件的拼接顺序。
        /// </summary>
        public static readonly IReadOnlyList<IMomoDataFilter> All =
        [
            new TenantDataFilter(),
            new SoftDeletedFilter(),
        ];
    }
}
