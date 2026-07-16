using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Viv.Elysia.Interface;

namespace Viv.Elysia.Request
{
    public abstract class ApiPagedRequestBase : ApiRequestBase, IApiPagedRequest
    {
        /// <summary>
        /// 当前页码
        /// </summary>
        [Display(Name = "当前页码")]
        [Range(1, double.MaxValue)]
        public int PageIndex { get; set; }

        /// <summary>
        /// 每页条数
        /// </summary>
        [Display(Name = "每页条数")]
        [Range(1, 10000)]
        public int PageSize { get; set; }

        /// <summary>
        /// 获取分页查询sql及请求参数
        /// </summary>
        /// <returns></returns>
        public abstract (string sql, object parameters) GetSqlQuery();
    }
}
