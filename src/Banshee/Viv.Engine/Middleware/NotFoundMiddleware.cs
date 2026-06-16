using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Viv.Delusion.Extension;

namespace Viv.Engine.Middleware
{
    /// <summary>
    /// 404统一处理中间件
    /// 区分Ajax/接口请求与页面请求，分别返回JSON和重定向页面
    /// </summary>
    public class NotFoundMiddleware
    {
        private readonly RequestDelegate _next;

        public NotFoundMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            await _next(context);
            if (context.Response.StatusCode == (int)HttpStatusCode.NotFound)
            {
                if (context.Request.IsAjax("/api"))
                {
                    await HandleApiNotFoundAsync(context);
                }
                else
                {
                    await HandlePageNotFoundAsync(context);
                }
            }
        }

        /// <summary>
        /// 处理接口/Ajax请求的404：返回JSON格式
        /// </summary>
        private static async Task HandleApiNotFoundAsync(HttpContext context)
        {
            var result = VivApiResult.ApiRsult(ApiResultCode.NotFound, "404 Not Found");

            context.Response.Clear();
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json;charset=UTF-8";
            await context.Response.WriteAsync(result.ToJson(), Encoding.UTF8);
        }

        /// <summary>
        /// 处理页面请求的404：重定向到404页面
        /// </summary>
        private static async Task HandlePageNotFoundAsync(HttpContext context)
        {
            context.Response.ContentType = "text/html;charset=utf-8";
            var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "404.html");
            if (File.Exists(path))
            {
                var html = await File.ReadAllTextAsync(path);
                await context.Response.WriteAsync(html);
            }
            else
            {
                await context.Response.WriteAsync("404 - 页面不存在");
            }
        }
    }
}
