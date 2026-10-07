using System;
using System.Collections.Generic;
using System.Linq;
using Viv.Delusion.Generic;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// 管理接口的分页助手：这几张配置表都只有几十行，整表取回再内存分页，
    /// 比为每张表写一条分页 SQL 更省事，也不会因各库方言不同而出偏差。
    /// </summary>
    public static class AdminPaging
    {
        /// <summary>
        /// 把已经排好序的全量结果切成 <see cref="PagedList{T}"/>
        /// </summary>
        /// <param name="ordered">已排序的全量数据</param>
        /// <param name="pageIndex">页码，从 1 开始</param>
        /// <param name="pageSize">每页条数，超出 200 按 200 收口</param>
        public static PagedList<T> Create<T>(IReadOnlyList<T> ordered, int pageIndex, int pageSize)
        {
            var size = pageSize <= 0 ? 20 : Math.Min(pageSize, 200);
            var index = pageIndex <= 0 ? 1 : pageIndex;
            var totalPages = (int)Math.Ceiling(ordered.Count / (double)size);

            return new PagedList<T>(index, size)
            {
                TotalCount = ordered.Count,
                TotalPages = totalPages,
                IsHaveFrontPage = index > 1,
                IsHaveNextPage = index < totalPages,
                Items = ordered.Skip((index - 1) * size).Take(size).ToList()
            };
        }
    }
}
