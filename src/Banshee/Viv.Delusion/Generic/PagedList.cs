using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Delusion.Generic
{
    public class PagedList<T>
    {
        public PagedList() { }

        public PagedList(int pagIndex, int pageSize)
        {
            PageIndex = pagIndex;
            PageSize = pageSize;
        }

        /// <summary>
        /// 页码
        /// </summary>
        public int PageIndex { get; set; } = 1;

        /// <summary>
        /// 每页数量
        /// </summary>
        public int PageSize { get; set; } = 20;

        /// <summary>
        /// 总页数
        /// </summary>
        public int TotalPages { get; set; }

        /// <summary>
        /// 总数据数量
        /// </summary>
        public long TotalCount { get; set; }

        /// <summary>
        /// 是否有上一页
        /// </summary>
        public bool IsHaveFrontPage { get; set; }

        /// <summary>
        /// 是否有下一页
        /// </summary>
        public bool IsHaveNextPage { get; set; }

        /// <summary>
        /// 数据集合
        /// </summary>
        public IEnumerable<T> Items { get; set; } = [];
    }
}
