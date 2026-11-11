using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Viv.Contracts.Enums;
using Viv.Delusion.Extension;

namespace Viv.Engine.Middleware
{
    /// <summary>
    /// [中间件] Viv Api 启动中间件
    ///
    /// 三个页面（welcome / gateway / notfound）现在是「静态骨架 + 内联 JSON + 一份共享 JS」：
    /// 服务端只负责把 <see cref="ServicePageState"/> 序列化进 <c>window.__VIV__</c>，
    /// 具体怎么显示全由 <c>web/app.js</c> 决定。
    ///
    /// 为什么不继续用占位符拼 HTML：加一个字段要同时改模板和这条 Replace 链，
    /// 而服务列表那种结构化数据还得在 C# 里拼标签、自己记得 HtmlEncode。
    /// 走 JSON 之后字段只在这里加一行，转义由序列化器负责，前端用 textContent 写入。
    /// </summary>
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
                    await context.SetApiResponseAsync(ApiResultCode.NotFound, (int)HttpStatusCode.OK);
                }
                else
                {
                    await LoadAppNotFoundPageAsync(context);
                }
            }
        }

        private static async Task LoadAppStartedPageAsync(HttpContext context)
        {
            // EnvOption 缺失（appsettings.json 未配该段）时兜底：没有它就没有服务名、机器Id 可显示
            var option = VivEngine.VivOptions?.EnvOption;
            var baseDir = AppContext.BaseDirectory;

            // 网关服务显示专属欢迎页 gateway.html，其余服务显示通用 welcome.html
            var isGateway = option != null && option.ServiceType == VivServiceType.Gateway;
            var templatePath = Path.Combine(baseDir, "web", isGateway ? "gateway.html" : "welcome.html");

            if (option == null || !File.Exists(templatePath))
            {
                await LoadAppNotFoundPageAsync(context);
                return;
            }

            var state = new ServicePageState
            {
                Kind = isGateway ? "gateway" : "welcome",
                ServiceName = option.ServiceName ?? "Service",
                Env = option.Env.ToString(),
                MachineId = option.MachineId,
                StartedAt = VivEngine.VivAppStartTime.GetValueOrDefault().ToUnixTime(true),
                DocsUrl = "/Scalar",
                HealthUrl = "/health",
                HomeUrl = "/",
                // 网关页列出 Aspire 已注册的服务（WithReference 注入的 services__* 环境变量）
                Services = isGateway ? BuildGatewayServices() : null,
            };

            await WritePageAsync(context, baseDir, templatePath, state);
        }

        private static async Task LoadAppNotFoundPageAsync(HttpContext context)
        {
            var baseDir = AppContext.BaseDirectory;
            var path = Path.Combine(baseDir, "web", "notfound.html");

            if (!File.Exists(path))
            {
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync($"404 - 页面不存在 (查找路径: {path})");
                return;
            }

            var state = new ServicePageState
            {
                Kind = "notfound",
                DocsUrl = "/Scalar",
                HealthUrl = "/health",
                HomeUrl = "/",
                // 404 最常见的成因是路径敲错，把用户实际访问的地址回显出来
                RequestPath = context.Request.Path.Value,
            };

            await WritePageAsync(context, baseDir, path, state);
        }

        /// <summary>
        /// 渲染页面：模板里的 <c>{SharedCss}</c> / <c>{SharedJs}</c> / <c>{State}</c> 三个占位符。
        ///
        /// <c>{State}</c> 必须最后替换 —— CSS 与 JS 里都有大量花括号，
        /// 但不会出现字面量 <c>{State}</c>，所以顺序上先塞资源、后塞数据最省心。
        ///
        /// JSON 用 <see cref="JsonSerializerOptions.Web"/>（camelCase，默认编码器）：
        /// 默认编码器会把 <c>&lt; &gt; &amp;</c> 转成 <c>\uXXXX</c>，
        /// 服务名里即使塞了 <c>&lt;/script&gt;</c> 也无法提前闭合内联脚本。
        /// **不要**换成 UnsafeRelaxedJsonEscaping —— 那等于自己开一个 XSS 口。
        /// </summary>
        private static async Task WritePageAsync(HttpContext context, string baseDir, string templatePath, ServicePageState state)
        {
            var html = await File.ReadAllTextAsync(templatePath);

            html = html
                .Replace("{SharedCss}", ReadWebAsset(baseDir, "app.css"))
                .Replace("{SharedJs}", ReadWebAsset(baseDir, "app.js"))
                .Replace("{State}", JsonSerializer.Serialize(state, JsonSerializerOptions.Web));

            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(html);
        }

        /// <summary>
        /// 网关页展示的服务列表：跳文档走网关自动生成的 /docs/{短名} 路由，不回源站地址。
        ///
        /// **必须带上文档名这一段**，不能只给 <c>/docs/{短名}/scalar/</c>：
        /// 那条路由的 catch-all 为空时，YARP 会把尾斜杠吃掉（<c>/scalar/</c> → <c>/scalar</c>），
        /// 下游 Scalar 收到无尾斜杠的地址会 302 到**相对路径** <c>scalar/</c>，
        /// 浏览器再基于 <c>/docs/apex/scalar/</c> 解析，就叠成了 <c>/docs/apex/scalar/scalar/</c>。
        /// 带一段 catch-all（<c>v1</c>）直达文档，绕开那次重定向。
        /// </summary>
        private static List<GatewayServiceLink> BuildGatewayServices()
            => AspireServiceDiscovery.Load()
                .Select(s => new GatewayServiceLink(
                    s.Name,
                    string.IsNullOrWhiteSpace(s.ShortName) ? s.Name : s.ShortName,
                    $"/docs/{s.ShortName}/scalar/{OpenApiDocumentName}"))
                .ToList();

        /// <summary>
        /// OpenAPI 文档名。<c>AddOpenApi()</c> 不传参时默认就是 v1（见 <c>VivStartApiExtensions</c> 的注册）。
        /// 哪天各服务开始用多文档，这里要改成按服务配置取。
        /// </summary>
        private const string OpenApiDocumentName = "v1";

        /// <summary>
        /// 读共享资源（app.css / app.js）。按文件修改时间缓存 —— 三个页面共用一份，
        /// 每次请求都读一遍 30KB 太浪费；开发时改完文件、下次请求就生效，不用重启。
        /// </summary>
        private static string ReadWebAsset(string baseDir, string fileName)
        {
            var path = Path.Combine(baseDir, "web", fileName);
            if (!File.Exists(path))
            {
                return string.Empty;
            }

            var stamp = File.GetLastWriteTimeUtc(path);

            lock (_assetLock)
            {
                if (_assetCache.TryGetValue(path, out var cached) && cached.Stamp == stamp)
                {
                    return cached.Text;
                }
            }

            var text = File.ReadAllText(path);

            lock (_assetLock)
            {
                _assetCache[path] = (stamp, text);
            }

            return text;
        }

        private static readonly Lock _assetLock = new();

        private static readonly Dictionary<string, (DateTime Stamp, string Text)> _assetCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 页面数据。<c>Kind</c> 目前前端没用到（三个页面各是独立模板），
        /// 留着是因为将来若合成单模板，它就是分流依据。
        /// </summary>
        private sealed class ServicePageState
        {
            public string Kind { get; set; } = string.Empty;

            public string? ServiceName { get; set; }

            public string? Env { get; set; }

            public int MachineId { get; set; }

            /// <summary>进程启动时刻（Unix 毫秒），前端据此每秒刷新运行时长</summary>
            public long StartedAt { get; set; }

            public string? DocsUrl { get; set; }

            public string? HealthUrl { get; set; }

            public string? HomeUrl { get; set; }

            public string? RequestPath { get; set; }

            public List<GatewayServiceLink>? Services { get; set; }
        }

        private sealed record GatewayServiceLink(string Name, string Label, string DocsUrl);
    }
}
