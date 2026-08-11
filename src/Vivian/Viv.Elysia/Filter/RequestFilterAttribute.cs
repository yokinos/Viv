using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Net;
using Viv.Elysia.Interface;
using Viv.Engine;
using Viv.Delusion.Extension;

namespace Viv.Elysia.Filter
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public class RequestFilterAttribute : Attribute, IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            // 无参 action（健康检查等）→ 无对象可校验，直接放行
            if (context.ActionArguments.IsNullOrEmpty())
            {
                await next().ConfigureAwait(false);
                return;
            }

            foreach (var (key, value) in context.ActionArguments)
            {
                // 参数值为空（请求未提供该参数）→ 判空拦截
                if (value is null)
                {
                    context.Result = VivApiResult.ApiRsult(ApiResultCode.ParamMissing, $"{key} 不能为空");
                    context.HttpContext.Response.StatusCode = (int)HttpStatusCode.OK;
                    return;
                }

                // 继承 IApiRequest 的参数 → 自动 Validate
                if (value is IApiRequest request)
                {
                    var errMsg = request.Validate();
                    if (!string.IsNullOrEmpty(errMsg))
                    {
                        context.Result = VivApiResult.ApiRsult(ApiResultCode.Error, errMsg);
                        context.HttpContext.Response.StatusCode = (int)HttpStatusCode.OK;
                        return;
                    }
                }
            }

            await next().ConfigureAwait(false);
        }
    }
}
