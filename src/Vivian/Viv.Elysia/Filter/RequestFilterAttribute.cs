using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Net;
using Viv.Engine;
using Viv.Vva.Extension;

namespace Viv.Elysia.Filter
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public class RequestFilterAttribute : Attribute, IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (context.ActionArguments.IsNullOrEmpty())
            {
                await next().ConfigureAwait(false);
                return;
            }

            foreach (var item in context.ActionArguments.Values)
            {
                if (item is IApiRequest request)
                {
                    var errMsg = request.Validate();
                    if (!string.IsNullOrEmpty(errMsg))
                    {
                        context.Result = VivApiResult.ApiRsult(ResultCode.Error, errMsg);
                        context.HttpContext.Response.StatusCode = (int)HttpStatusCode.OK;
                        return;
                    }
                }
            }

            await next().ConfigureAwait(false);
        }
    }
}