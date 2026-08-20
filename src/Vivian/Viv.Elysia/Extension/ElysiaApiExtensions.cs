using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Elysia.Filter;

namespace Viv.Elysia.Extension
{
    public static class ElysiaApiExtensions
    {
        /// <summary>
        /// 添加自定义的过滤器
        /// </summary>
        /// <typeparam name="TFilterType"></typeparam>
        /// <param name="filters"></param>
        /// <returns></returns>
        public static FilterCollection AddElysiaFilter(this FilterCollection filters)
        {
            filters.Add<RequestFilterAttribute>();
            filters.Add<OperationLogFilterAttribute>();
            return filters;
        }
    }
}
