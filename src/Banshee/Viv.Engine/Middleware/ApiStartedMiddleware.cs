using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Viv.Delusion.Extension;

namespace Viv.Engine.Middleware
{
    public class ApiStartedMiddleware
    {
        private readonly RequestDelegate _next;

        public ApiStartedMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            if (context.Request.Path.HasValue && context.Request.Path.Value == "/")
            {
                await LoadAppStartedPageAsync(context);
                return;
            }

            await _next(context);

            if (context.Response.StatusCode == (int)HttpStatusCode.NotFound)
            {
                if (context.Request.IsAjax("/api"))
                {
                    await context.SetApiResponseAsync(ApiResultCode.NotFound);
                }
                else
                {
                    await LoadAppNotFoundPageAsync(context);
                }
            }
        }

        private static async Task LoadAppStartedPageAsync(HttpContext context)
        {
            context.Response.ContentType = "text/html; charset=utf-8";

            var baseDir = AppContext.BaseDirectory;
            var path = Path.Combine(baseDir, "app_started.html");

            if (!File.Exists(path))
            {
                await LoadAppNotFoundPageAsync(context);
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

        private static async Task LoadAppNotFoundPageAsync(HttpContext context)
        {
            context.Response.ContentType = "text/html; charset=utf-8";

            var baseDir = AppContext.BaseDirectory;
            var path = Path.Combine(baseDir, "app_404.html");
            if (!File.Exists(path))
            {
                await context.Response.WriteAsync($"404 - 页面不存在 (查找路径: {path})");
                return;
            }

            await context.Response.WriteAsync(await File.ReadAllTextAsync(path));
        }
    }
}
