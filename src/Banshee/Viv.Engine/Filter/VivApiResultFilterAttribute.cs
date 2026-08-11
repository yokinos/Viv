using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Viv.Delusion.Extension;

namespace Viv.Engine.Filter
{
    /// <summary>
    /// VivApiResult过滤器（占位）。HTTP 状态码透传逻辑在 <see cref="VivApiResult.ExecuteResultAsync"/> 内按
    /// <see cref="VivRunDefine.AllowedHttpStatusCodes"/> 白名单处理（响应在结果执行前未提交，那时才能改状态码）。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class VivApiResultFilterAttribute : Attribute, IAsyncResultFilter
    {
        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            await next();
        }
    }
}
