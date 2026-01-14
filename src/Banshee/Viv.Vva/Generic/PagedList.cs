using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Vva.Generic
{
    public class PagedList<T>
    {
        public PagedList()
        {

        }

        /// <summary>
        /// 页码
        /// </summary>
        public int PageIndex { get; set; }

        /// <summary>
        /// 每页数量
        /// </summary>
        public long PageSize { get; set; }

        /// <summary>
        /// 总页数
        /// </summary>
        public int TotalPages { get; set; }

        /// <summary>
        /// 总数据数量
        /// </summary>
        public long TotalItems { get; set; }

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
