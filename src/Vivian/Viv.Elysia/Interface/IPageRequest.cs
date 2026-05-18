using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Elysia.Interface
{
    public interface IPageRequest : IApiRequest
    {
        /// <summary>
        /// 分页请求页码
        /// </summary>
        public int PageIndex { get; set; }

        /// <summary>
        /// 分页请求每页数量
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// 获取分页查询的SQL语句
        /// </summary>
        /// <returns></returns>
        string GetPageSql();
    }
}
