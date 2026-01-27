using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Interface;

namespace Viv.Engine.Middleware
{
    /// <summary>
    /// [中间件] 注册当前请求的VivContext
    /// </summary>
    public class VivContextMiddleware
    {
        private readonly RequestDelegate _next;

        public VivContextMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, IVivContext vivContext)
        {
            try
            {


                vivContext.TenantId = 0;
                await _next(context);
            }
            finally
            {
                vivContext.Clear();
            }
        }
    }
}
