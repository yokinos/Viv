using Microsoft.AspNetCore.Http;
using System;
using System.Collections;
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

            var option = VivEngine.VivOptions.EnvOption;
            var baseDir = AppContext.BaseDirectory;

            // 网关服务显示专属欢迎页 gateway.html，其余服务显示通用 welcome.html
            var isGateway = option != null && option.ServiceType == VivServiceType.Gateway;
            var path = Path.Combine(baseDir, "web", isGateway ? "gateway.html" : "welcome.html");

            if (!File.Exists(path))
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
                html = html.Replace("{ServiceList}", BuildGatewayServiceListHtml(LoadGatewayServices()));
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

        private const string ServicesEnvPrefix = "services__";

        /// <summary>
        /// 枚举 Aspire 注入的 services__* 环境变量，得到被引用服务的可点击地址列表。
        /// 仅当网关由 AppHost 启动（WithReference）时才有数据；独立启动时为空。
        /// </summary>
        private static List<GatewayServiceInfo> LoadGatewayServices()
        {
            var found = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                var key = entry.Key?.ToString();
                var value = entry.Value?.ToString();
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value) ||
                    !key.StartsWith(ServicesEnvPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = SplitServiceName(key.Substring(ServicesEnvPrefix.Length));
                if (string.IsNullOrEmpty(name) || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
                {
                    continue;
                }

                // 同一服务有多个端点（http/https）时优先 http，便于浏览器直接访问
                if (!found.TryGetValue(name, out var existing) ||
                    (uri.Scheme == Uri.UriSchemeHttp && existing.Scheme != Uri.UriSchemeHttp))
                {
                    found[name] = uri;
                }
            }

            return found
                .Select(kv => new GatewayServiceInfo(kv.Key, kv.Value))
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// 去掉 services__ 之后末尾的端点描述段，还原服务名：
        ///   viv-apex-api__http__0         -> viv-apex-api
        ///   viv-apex-api__0               -> viv-apex-api
        ///   viv-apex-api__default__0      -> viv-apex-api
        ///   viv-apex-api__0__endpoints__0 -> viv-apex-api
        /// </summary>
        private static string? SplitServiceName(string remainder)
        {
            for (var i = 0; i < 3; i++)
            {
                var idx = remainder.LastIndexOf("__", StringComparison.Ordinal);
                if (idx <= 0)
                {
                    break;
                }

                var segment = remainder.Substring(idx + 2);
                if (segment.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("https", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("default", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("endpoints", StringComparison.OrdinalIgnoreCase) ||
                    segment.All(char.IsDigit))
                {
                    remainder = remainder.Substring(0, idx);
                }
                else
                {
                    break;
                }
            }

            return remainder;
        }

        /// <summary>
        /// 把服务列表渲染为 gateway.html 的 {ServiceList} 片段；无服务时返回空串（隐藏整个区块）。
        /// </summary>
        private static string BuildGatewayServiceListHtml(List<GatewayServiceInfo> services)
        {
            if (services.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.Append("<div class=\"service-list\">");
            foreach (var s in services)
            {
                // 文档经网关访问（viv.yarp.json 的 /docs/{服务名} 路由），不跳转服务自身地址。
                // 尾斜杠避免下游 302 /scalar -> /scalar/ 后浏览器请求落到网关根路径。
                sb.Append("<a class=\"service-tag\" href=\"")
                  .Append("/docs/")
                  .Append(WebUtility.HtmlEncode(s.Name))
                  .Append("/scalar/\" target=\"_blank\" rel=\"noopener\"><span class=\"status\"></span>")
                  .Append(WebUtility.HtmlEncode(s.Name))
                  .Append("<span class=\"arrow\">→</span></a>");
            }
            sb.Append("</div>");
            return sb.ToString();
        }

        private sealed record GatewayServiceInfo(string Name, Uri Uri);
    }
}
