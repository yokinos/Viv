using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Viv.Contracts.Enums;
using Viv.Delusion.Extension;
using Viv.Engine.Options;

namespace Viv.Engine.Middleware
{
    public class ApiStartedMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IWebHostEnvironment _hostEnvironment;

        public ApiStartedMiddleware(RequestDelegate next, IWebHostEnvironment hostEnvironment)
        {
            _next = next;
            _hostEnvironment = hostEnvironment;
        }

        public async Task Invoke(HttpContext context)
        {
            if (context.Request.Path.HasValue && context.Request.Path.Value == "/")
            {
                await LoadAppStartedPageAsync(context);
                return;
            }

            await _next(context);
        }

        private static async Task LoadAppStartedPageAsync(HttpContext context)
        {
            context.Response.ContentType = "text/html; charset=utf-8";

            // 使用 AppContext.BaseDirectory 获取运行目录
            var baseDir = AppContext.BaseDirectory;
            var path = Path.Combine(baseDir, "app_started.html");

            if (!File.Exists(path))
            {
                await context.Response.WriteAsync($"404 - 页面不存在 (查找路径: {path})");
                return;
            }

            var option = VivEngine.VivOptions.EnvOption;
            var startTime = VivEngine.VivAppStartTime.GetValueOrDefault();


            var htmlTemplate = await File.ReadAllTextAsync(path);
            var html = htmlTemplate
                .Replace("{MachineId}", option.MachineId.ToString())
                .Replace("{Env}", option.Env.ToString())
                .Replace("{StartTimeMs}", startTime.ToUnixTime(true).ToString())
                .Replace("{ServiceName}", option.ServiceName ?? "Service");

            await context.Response.WriteAsync(html);
        }
    }
}
