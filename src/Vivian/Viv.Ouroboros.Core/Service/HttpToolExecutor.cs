using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Viv.Echo.Http;
using Viv.Entity.Database.Ouroboros;
using Viv.Entity.Enums;
using Viv.Log;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// HTTP 工具（<see cref="EmToolTransport.Http"/>）的通用执行器。不通业务，只负责"把入参发出去、把响应文本拿回来"。
    ///
    /// <c>OtTool</c> 上没有 method / 超时字段，所以约定：<see cref="OtTool.Endpoint"/> 写成
    /// <c>"GET https://host/path"</c> 时用该动词，不写动词按 POST；超时统一用 <see cref="DefaultTimeout"/>。
    /// GET 把入参铺成 query，POST 把入参序列化成 JSON body。
    ///
    /// 请求走框架的 <see cref="IVivHttpService"/>（GET/POST 两个动词）。本执行器会被 AgentFactory
    /// 缓存 60 秒的工具闭包长期持有，而那个服务是 Scoped，所以这里只持作用域工厂，**每次调用现开作用域**解析。
    ///
    /// 非 2xx **不当异常抛**：状态码与响应体原样交回模型，由它自己决定重试还是改参数 ——
    /// 抛出去只会把一整轮对话打死，而模型本来有能力自己纠正。
    /// </summary>
    public class HttpToolExecutor
    {
        /// <summary>OtTool 没有超时列，这里给一个统一上限</summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

        private readonly ILoggerContract _logger;
        private readonly IServiceScopeFactory? _scopeFactory;
        private readonly Func<IVivHttpService?>? _serviceProvider;

        /// <summary>
        /// 构造函数（生产用）：只持作用域工厂 —— <see cref="IVivHttpService"/> 是 Scoped，
        /// 钉进被缓存的工具闭包就等于跨请求复用一个早就释放的作用域。
        /// </summary>
        /// <param name="logger">日志（Singleton）</param>
        /// <param name="scopeFactory">作用域工厂（Singleton，可以安全地被缓存住的工具闭包持有）</param>
        public HttpToolExecutor(ILoggerContract logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        /// <summary>
        /// 构造函数（测试用）：直接给服务的来源，省掉 DI 作用域；返回 null 表示宿主没启用 HTTP。
        /// </summary>
        /// <param name="logger">日志</param>
        /// <param name="serviceProvider">每次调用取一次 HTTP 服务</param>
        public HttpToolExecutor(ILoggerContract logger, Func<IVivHttpService?> serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// 按工具定义发一次请求。任何失败都以 <see cref="HttpToolResult.Text"/> 的形式返回给模型，
        /// 本方法不抛异常（取消除外，取消要让它继续往上冒）。
        /// </summary>
        /// <param name="tool">工具定义（用其中的 Endpoint）</param>
        /// <param name="arguments">模型给的原始入参</param>
        /// <param name="cancellationToken">取消令牌</param>
        public async Task<HttpToolResult> SendAsync(OtTool tool, IEnumerable<KeyValuePair<string, object?>> arguments,
            CancellationToken cancellationToken)
        {
            var endpoint = tool.Endpoint?.Trim();
            if (string.IsNullOrEmpty(endpoint))
            {
                _logger.Warning("HTTP 工具没有配置 Endpoint：{0}", tool.ToolKey);
                return new HttpToolResult("该工具没有配置调用地址（Endpoint），无法执行。", false, "Endpoint 为空", 0);
            }

            var payload = arguments.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            var (method, url) = SplitEndpoint(endpoint);

            // 只有异常路径用得上：正常路径的耗时取服务报的 ElapsedTime，它不含外层调度开销
            var fallback = Stopwatch.StartNew();
            IServiceScope? scope = null;

            try
            {
                var http = Rent(out scope);
                if (http is null)
                {
                    _logger.Warning("HTTP 工具解析不到 IVivHttpService（宿主没启用 EchoOption.EnableHttp）：{0}", tool.ToolKey);
                    return new HttpToolResult("宿主没有启用 HTTP 服务（EchoOption.EnableHttp），无法调用该工具。",
                        false, "IVivHttpService 未注册", 0);
                }

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(DefaultTimeout);

                var result = method == HttpMethod.Get
                    ? await http.GetAsync<string>(url, BuildQuery(payload), null, timeoutCts.Token)
                    : method == HttpMethod.Post
                        ? await http.PostAsync<string>(url, BuildBody(payload), null, timeoutCts.Token)
                        : await SendDirectAsync(http.HttpClient, method, url, payload, timeoutCts.Token);

                if (result.IsSuccess) return new HttpToolResult(result.ResponseJson ?? string.Empty, true, null, result.ElapsedTime);

                var status = (int)result.StatusCode;
                _logger.Warning("HTTP 工具返回非 2xx：{0} → {1}", tool.ToolKey, status);

                // 状态码必须让模型看见（它据此决定重试还是改参数），响应体是它唯一能拿来纠正的信息
                var detail = string.IsNullOrWhiteSpace(result.ResponseJson) ? result.Message : result.ResponseJson;
                return new HttpToolResult($"HTTP {status} {detail}".TrimEnd(), false, $"HTTP {status}", result.ElapsedTime);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.Warning("HTTP 工具超时：{0}（{1} 秒）", tool.ToolKey, DefaultTimeout.TotalSeconds);
                return new HttpToolResult($"调用超时（超过 {DefaultTimeout.TotalSeconds} 秒）。", false, "超时", fallback.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                // 地址写错、DNS 解析不了、对端拒连、替身直接抛都在这里；交回模型而不是炸掉整轮
                _logger.Warning("HTTP 工具调用失败：{0}，{1}", tool.ToolKey, ex.Message);
                return new HttpToolResult($"调用失败：{ex.Message}", false, ex.Message, fallback.ElapsedMilliseconds);
            }
            finally
            {
                scope?.Dispose();
            }
        }

        /// <summary>
        /// 借一个 HTTP 服务。必须每次调用新开作用域：服务是 Scoped，而持有本执行器的工具闭包
        /// 会被缓存 60 秒、跨请求复用（与 ToolCallRecorder 同一手法）。
        /// </summary>
        private IVivHttpService? Rent(out IServiceScope? scope)
        {
            if (_serviceProvider is not null)
            {
                scope = null;
                return _serviceProvider();
            }

            scope = _scopeFactory!.CreateScope();
            return scope.ServiceProvider.GetService<IVivHttpService>();
        }

        /// <summary>
        /// Endpoint 拆成动词 + 地址。约定写法 <c>"GET https://..."</c>；没有可识别动词时整串当地址、动词按 POST。
        /// </summary>
        private static (HttpMethod Method, string Url) SplitEndpoint(string endpoint)
        {
            var separator = endpoint.IndexOf(' ');
            if (separator > 0)
            {
                var url = endpoint[(separator + 1)..].Trim();
                if (url.Length > 0 && TryParseMethod(endpoint[..separator], out var method)) return (method, url);
            }

            return (HttpMethod.Post, endpoint);
        }

        private static bool TryParseMethod(string text, out HttpMethod method)
        {
            switch (text.ToUpperInvariant())
            {
                case "GET": method = HttpMethod.Get; return true;
                case "POST": method = HttpMethod.Post; return true;
                case "PUT": method = HttpMethod.Put; return true;
                case "DELETE": method = HttpMethod.Delete; return true;
                case "PATCH": method = HttpMethod.Patch; return true;
                case "HEAD": method = HttpMethod.Head; return true;
                case "OPTIONS": method = HttpMethod.Options; return true;
                default: method = HttpMethod.Post; return false;
            }
        }

        /// <summary>GET 的入参铺成 query。接口只收字符串字典，值按 <see cref="ToQueryValue"/> 转。</summary>
        private static Dictionary<string, string> BuildQuery(Dictionary<string, object?> payload)
        {
            var query = new Dictionary<string, string>(payload.Count, StringComparer.Ordinal);
            foreach (var pair in payload) query[pair.Key] = ToQueryValue(pair.Value);
            return query;
        }

        /// <summary>
        /// POST 的入参。值是 <see cref="JsonElement"/>（模型给的）时必须先摊成基元类型：
        /// 服务的序列化走 Newtonsoft，它不认 JsonElement，会写成 <c>{"ValueKind":3}</c> 这种垃圾 body。
        /// </summary>
        private static Dictionary<string, object?> BuildBody(Dictionary<string, object?> payload)
        {
            var body = new Dictionary<string, object?>(payload.Count, StringComparer.Ordinal);
            foreach (var pair in payload) body[pair.Key] = ToPlainValue(pair.Value);
            return body;
        }

        /// <summary>
        /// GET/POST 之外的动词走 <see cref="IVivHttpService.HttpClient"/>：接口只封了那两个动词，
        /// 而 Endpoint 的既有约定允许 PUT/DELETE/PATCH/HEAD —— 少一个就等于老工具静默失效。
        /// 这条路没有 <see cref="HttpResult{T}"/>，只能自己掐表。
        /// </summary>
        private static async Task<HttpResult<string>> SendDirectAsync(HttpClient client, HttpMethod method, string url,
            Dictionary<string, object?> payload, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();

            using var request = new HttpRequestMessage(method, url);
            if (payload.Count > 0)
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();

            return new HttpResult<string>
            {
                IsSuccess = response.IsSuccessStatusCode,
                StatusCode = response.StatusCode,
                Message = response.ReasonPhrase,
                ResponseJson = body,
                ElapsedTime = stopwatch.ElapsedMilliseconds
            };
        }

        private static string ToQueryValue(object? value)
        {
            switch (value)
            {
                case null: return string.Empty;
                case JsonElement element: return element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.GetRawText();
                case string text: return text;
                default: return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
        }

        private static object? ToPlainValue(object? value)
        {
            if (value is not JsonElement element) return value;

            switch (element.ValueKind)
            {
                case JsonValueKind.String: return element.GetString();
                case JsonValueKind.Number:
                    // 整数必须留成整数：走 double 会写成 3.0，对端按 int 解析就炸
                    if (element.TryGetInt64(out var integer)) return integer;
                    return element.GetDouble();
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.Null: return null;
                case JsonValueKind.Array:
                    return element.EnumerateArray().Select(item => ToPlainValue(item)).ToList();
                case JsonValueKind.Object:
                    return element.EnumerateObject()
                        .ToDictionary(property => property.Name, property => ToPlainValue(property.Value), StringComparer.Ordinal);
                default: return null;
            }
        }
    }

    /// <summary>
    /// HTTP 工具的调用结果。<see cref="Text"/> 是回给模型看的正文（失败时带状态码与响应体），
    /// <see cref="Succeeded"/> / <see cref="Error"/> 只用于留痕，不参与模型决策；
    /// <see cref="LatencyMs"/> 取服务的 <c>HttpResult.ElapsedTime</c>，比外层掐表准（不含调度与留痕开销）。
    /// </summary>
    public sealed record HttpToolResult(string Text, bool Succeeded, string? Error, long LatencyMs);
}
