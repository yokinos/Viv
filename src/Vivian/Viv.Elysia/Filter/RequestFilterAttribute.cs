using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Linq;
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
            // 找出声明为 IApiRequest 的参数（接口必有请求体）；无则无对象可校验，直接放行
            var requestParams = context.ActionDescriptor.Parameters
                .Where(p => typeof(IApiRequest).IsAssignableFrom(p.ParameterType))
                .ToList();

            if (requestParams.Count == 0)
            {
                await next().ConfigureAwait(false);
                return;
            }

            foreach (var param in requestParams)
            {
                // 声明了请求体参数但请求未提供（body 缺失 → 参数为 null）→ 判空拦截，避免 action 收到 null 请求体
                if (!context.ActionArguments.TryGetValue(param.Name, out var value) || value is null)
                {
                    context.Result = VivApiResult.ApiRsult(ApiResultCode.ParamMissing, $"{param.Name} 不能为空");
                    context.HttpContext.Response.StatusCode = (int)HttpStatusCode.OK;
                    return;
                }

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
