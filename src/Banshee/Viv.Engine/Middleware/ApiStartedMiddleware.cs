using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Viv.Contracts.Enums;
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

        public async Task InvokeAsync(HttpContext context)
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

            // EnvOption 缺失（viv.config.json 未配该段）时兜底，避免下方 .Replace 链对 option 无条件解引用 NRE
            var option = VivEngine.VivOptions?.EnvOption;
            var baseDir = AppContext.BaseDirectory;

            // 网关服务显示专属欢迎页 gateway.html，其余服务显示通用 welcome.html
            var isGateway = option != null && option.ServiceType == VivServiceType.Gateway;
            var path = Path.Combine(baseDir, "web", isGateway ? "gateway.html" : "welcome.html");

            if (option == null || !File.Exists(path))
            {
                await LoadAppNotFoundPageAsync(context);
                return;
            }

            var startTime = VivEngine.VivAppStartTime.GetValueOrDefault();

            var htmlTemplate = await File.ReadAllTextAsync(path);
            var html = htmlTemplate
                .Replace("{MachineId}", option.MachineId.ToString())
                .Replace("{Env}", option.Env.ToString())
                .Replace("{StartTimeMs}", startTime.ToUnixTime(true).ToString())
                .Replace("{ServiceName}", option.ServiceName ?? "Service");

            // 网关页展示 Aspire 已注册的服务（WithReference 注入的 services__* 环境变量），点击经网关打开各服务 Scalar 文档
            if (isGateway)
            {
                html = html.Replace("{ServiceList}", BuildGatewayServiceListHtml(AspireServiceDiscovery.Load()));
            }

            await context.Response.WriteAsync(html);
        }

        private static async Task LoadAppNotFoundPageAsync(HttpContext context)
        {
            context.Response.ContentType = "text/html; charset=utf-8";

            var baseDir = AppContext.BaseDirectory;
            var path = Path.Combine(baseDir, "web", "notfound.html");
            if (!File.Exists(path))
            {
                await context.Response.WriteAsync($"404 - 页面不存在 (查找路径: {path})");
                return;
            }

            await context.Response.WriteAsync(await File.ReadAllTextAsync(path));
        }

        /// <summary>
        /// 把服务列表渲染为 gateway.html 的 {ServiceList} 片段；无服务时返回空串（隐藏整个区块）。
        /// </summary>
        private static string BuildGatewayServiceListHtml(List<AspireServiceDiscovery.GatewayService> services)
        {
            if (services.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.Append("<div class=\"service-list\">");
            foreach (var s in services)
            {
                // 文档经网关访问（自动生成的 /docs/{短名} 路由），不跳转服务自身地址。
                // 尾斜杠避免下游 302 /scalar -> /scalar/ 后浏览器请求落到网关根路径。
                var label = string.IsNullOrWhiteSpace(s.ShortName) ? s.Name : s.ShortName;
                sb.Append("<a class=\"service-tag\" href=\"")
                  .Append("/docs/")
                  .Append(WebUtility.HtmlEncode(s.ShortName))
                  .Append("/scalar/\" target=\"_blank\" rel=\"noopener\"><span class=\"status\"></span>")
                  .Append(WebUtility.HtmlEncode(label))
                  .Append("<span class=\"arrow\">→</span></a>");
            }
            sb.Append("</div>");
            return sb.ToString();
        }
    }
}
