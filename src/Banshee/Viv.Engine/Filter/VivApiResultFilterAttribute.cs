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
    /// VivApiResult过滤器
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class VivApiResultFilterAttribute : Attribute, IAsyncResultFilter
    {
        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            var result = context.Result;
            if (result is IVivApiResult vivApiResult)
            {
                if (vivApiResult.RequestId.IsNullOrEmpty())
                {
                    vivApiResult.RequestId = context.HttpContext.TraceIdentifier;
                }
            }
            await next();
        }
    }
}
