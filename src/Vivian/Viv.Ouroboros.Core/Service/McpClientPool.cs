using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Viv.Contracts.Attributes;
using Viv.Contracts.Enums;
using Viv.Contracts.Interface;
using Viv.Entity.Database.Ouroboros;
using Viv.Entity.Enums;
using Viv.Log;
using Viv.Momo;
using Viv.Ouroboros.Core.Entity.Model.Agent;
using Viv.Ouroboros.Core.IService;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// MCP 客户端连接池。<b>单例</b>：MCP 连接是长连接、长生命周期资源（stdio 还挂着子进程），
    /// 按服务名缓存并复用，绝不跟着"每请求一个作用域"起落。
    ///
    /// 与工具闭包的关系：装配好的工具函数（<see cref="McpToolFunction"/>）只持本池与两个名字，
    /// 不持 <c>McpClient</c>/<c>McpClientTool</c>，也不持任何 Scoped 服务 —— 工具闭包会被
    /// <see cref="ToolRegistry"/> 缓存 60 秒并跨请求复用，钉进去就等于 captive dependency。
    /// 本池要读库，所以每次现开作用域（与 <see cref="ToolCallRecorder"/> 同一手法）。
    ///
    /// 失效：连接条目的键是"传输 + 地址 + 请求头"三样的指纹，配置改了指纹就对不上，下次自动重连；
    /// 另外 <see cref="ToolRegistry.InvalidateAll"/> 会把整池清掉（改 MCP 配置走的是那条路），
    /// 免得停用/删除服务后还留着一条连接或一个 stdio 子进程。
    ///
    /// 失败策略：连接失败按 2/4/8/16/30 秒指数退避，退避期内直接快速失败并说明还剩几秒；
    /// 调用中断连则把该连接作废，<b>下一次调用</b>重连（当次调用绝不自动重试 —— 工具的副作用未必幂等，
    /// 重试一次可能就真的执行两遍）。所有失败都以 <see cref="ToolExecutionException"/> 抛出，
    /// 由 <see cref="RegisteredToolFunction"/> 落一条失败的 <c>OtToolCall</c> 并把原因当工具结果交回模型。
    ///
    /// <c>OtMcpServer.Headers</c> 按实体注释是放内部令牌的地方：本类只在建连时读它，
    /// 既不写日志也不回任何接口（指纹串里含它，但指纹只做相等比较，从不外泄）。
    ///
    /// 注册：<c>AsSelf = true</c> 是必须的 —— 自动注册的默认口径是 <c>AsImplementedInterfaces()</c>，
    /// 而本类实现了 <see cref="IDisposable"/>/<see cref="IAsyncDisposable"/>，那两下会把注册名改成
    /// "IDisposable/IAsyncDisposable"，按具体类型就解析不到了。AsSelf 不影响容器对单例的释放跟踪。
    /// </summary>
    [VivDependency(Lifetime = DependencyLifetime.Singleton, AsSelf = true)]
    public sealed class McpClientPool : IDependency, IAsyncDisposable, IDisposable
    {
        /// <summary>建连（含握手与列工具）的上限：命令行敲错、地址不通时不要挂死调用方</summary>
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);

        /// <summary>单次工具调用的上限。OtMcpServer 没有超时列，这里给一个统一上限</summary>
        private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(60);

        /// <summary>首次连接失败后的退避时长</summary>
        private static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(2);

        /// <summary>退避上限：连不上就一直等下去没意义，30 秒足够"运维改完配置再点一次"</summary>
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILoggerContract _logger;

        /// <summary>按服务名的连接条目（含"刚失败、正在退避"的占位条目）</summary>
        private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

        /// <summary>按服务名的建连闸门：同一服务的并发装配只允许一个真去连，其余等结果</summary>
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

        private int _disposed;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="scopeFactory">作用域工厂（单例可以安全持有；读库现开作用域）</param>
        /// <param name="logger">日志</param>
        public McpClientPool(IServiceScopeFactory scopeFactory, ILoggerContract logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        /// <summary>
        /// 连上该服务并列出工具（供装配期发现工具用）。连不上就抛 <see cref="ToolExecutionException"/>，
        /// 由调用方决定是"跳过这次装配"还是"当失败回给模型"。
        /// </summary>
        /// <param name="server">服务配置行</param>
        /// <param name="cancellationToken">取消令牌</param>
        public async Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(OtMcpServer server, CancellationToken cancellationToken)
        {
            var entry = await EnsureAsync(server, cancellationToken).ConfigureAwait(false);
            return entry.Descriptors;
        }

        /// <summary>
        /// 调一次 MCP 工具，返回给模型看的正文。失败一律抛 <see cref="ToolExecutionException"/>
        /// （正文即原因），不抛裸异常 —— 裸异常会顺着 MAF 回环打死一整轮对话。
        /// </summary>
        /// <param name="serverName">服务名（OtMcpServer.ServerName）</param>
        /// <param name="toolName">服务侧的原始工具名</param>
        /// <param name="arguments">模型给的入参</param>
        /// <param name="cancellationToken">取消令牌</param>
        public async Task<string> InvokeAsync(string serverName, string toolName, IReadOnlyDictionary<string, object?> arguments,
            CancellationToken cancellationToken)
        {
            var entry = await EnsureAsync(serverName, cancellationToken).ConfigureAwait(false);

            if (!entry.Tools.TryGetValue(toolName, out var tool))
                throw new ToolExecutionException(
                    $"MCP 服务 {serverName} 当前没有名为 {toolName} 的工具（可能已被服务端改掉），请不要再调用它。", "工具不存在");

            var startedAt = Stopwatch.GetTimestamp();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CallTimeout);

            try
            {
                var result = await tool.CallAsync(arguments, cancellationToken: timeout.Token).ConfigureAwait(false);
                var latency = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                var text = ExtractText(result);

                if (result.IsError == true)
                {
                    // MCP 的"工具报错"不是异常，是结果里的 isError 标记：留痕记失败，正文交回模型
                    _logger.Warning("MCP 工具返回错误：{0} → {1}", serverName, toolName);
                    throw new ToolExecutionException(
                        string.IsNullOrWhiteSpace(text) ? $"MCP 工具 {toolName} 执行失败（服务端未给原因）。" : text,
                        "MCP 工具报错", latency);
                }

                return text;
            }
            catch (ToolExecutionException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 用户/上层主动取消：不是工具失败，别留痕成 Failed，原样往上冒
                throw;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                var latency = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                _logger.Warning("MCP 工具超时：{0} → {1}（{2} 秒）", serverName, toolName, CallTimeout.TotalSeconds);
                Break(serverName, new TimeoutException($"超过 {CallTimeout.TotalSeconds} 秒未返回"));
                throw new ToolExecutionException($"调用 MCP 工具 {toolName} 超时（超过 {CallTimeout.TotalSeconds} 秒）。", "超时", latency);
            }
            catch (Exception ex)
            {
                var latency = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                _logger.Warning("MCP 工具调用失败：{0} → {1}，{2}", serverName, toolName, ex.Message);

                // 连接可能已经断了：作废它，让**下一次**调用重连（当次不重试，避免重复副作用）
                Break(serverName, ex);
                throw new ToolExecutionException($"调用 MCP 工具 {toolName} 失败：{ex.Message}", ex.Message, latency);
            }
        }

        /// <summary>
        /// 作废某个服务的连接：停用/改了配置/断连后调用。释放是"发出去就不等"，
        /// 因为失败路径上不能阻塞（stdio 子进程的收尾由 SDK 自己完成）。
        /// </summary>
        /// <param name="serverName">服务名</param>
        public void Invalidate(string serverName)
        {
            if (string.IsNullOrWhiteSpace(serverName)) return;
            if (_entries.TryRemove(serverName, out var entry) && entry.Client is not null) Release(entry.Client, serverName);
        }

        /// <summary>清空整池（配置全量失效时走这条）</summary>
        public void InvalidateAll()
        {
            var count = 0;
            foreach (var key in _entries.Keys.ToList())
            {
                if (!_entries.TryRemove(key, out var entry)) continue;
                count++;
                if (entry.Client is not null) Release(entry.Client, key);
            }

            if (count > 0) _logger.Info("MCP 连接已全部释放（{0} 个服务）", count);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            foreach (var key in _entries.Keys.ToList())
            {
                if (!_entries.TryRemove(key, out var entry) || entry.Client is null) continue;

                try
                {
                    await entry.Client.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Warning("释放 MCP 连接失败（忽略）：{0}，{1}", key, ex.Message);
                }
            }

            foreach (var gate in _gates.Values) gate.Dispose();
            _gates.Clear();
        }

        /// <inheritdoc />
        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        /// <summary>
        /// 拿一个可用的连接：已连上且配置指纹对得上就直接复用；否则（含退避结束后的重连）现连一次。
        /// 退避期内直接失败，不再去打网络/拉子进程。
        /// </summary>
        private async Task<Entry> EnsureAsync(OtMcpServer server, CancellationToken cancellationToken)
        {
            var fingerprint = BuildFingerprint(server);

            if (TryGetUsable(server.ServerName, fingerprint, out var ready)) return ready;

            var gate = _gates.GetOrAdd(server.ServerName, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (TryGetUsable(server.ServerName, fingerprint, out ready)) return ready;

                if (_entries.TryGetValue(server.ServerName, out var stale))
                {
                    if (string.Equals(stale.Fingerprint, fingerprint, StringComparison.Ordinal) && DateTime.UtcNow < stale.NextAttemptAt)
                    {
                        var wait = (int)Math.Ceiling((stale.NextAttemptAt - DateTime.UtcNow).TotalSeconds);
                        throw new ToolExecutionException(
                            $"MCP 服务 {server.ServerName} 连续连接失败，{wait} 秒内不再重试：{stale.LastError}", "MCP 服务不可用");
                    }

                    // 指纹不同 = 配置改过，旧连接作废；退避已过 = 该再试一次了
                    Invalidate(server.ServerName);
                }

                return await ConnectAsync(server, fingerprint, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>按服务名重新读一次配置行再连（调用期只有名字，没有配置行）</summary>
        private async Task<Entry> EnsureAsync(string serverName, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetService<IMomoDbContext>();
            if (db is null) throw new ToolExecutionException("解析不到数据库上下文，无法读取 MCP 服务配置。", "IMomoDbContext 未注册");

            var server = await db.SingleOrDefaultAsync<OtMcpServer>(x => x.ServerName == serverName && x.IsEnabled)
                .ConfigureAwait(false);

            if (server is null)
                throw new ToolExecutionException($"MCP 服务不存在或未启用：{serverName}（请检查 api/McpServers 注册与绑定）", "服务未注册");

            return await EnsureAsync(server, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>条目可用 = 指纹一致 + 已连上 + 不在退避期</summary>
        private bool TryGetUsable(string serverName, string fingerprint, out Entry entry)
        {
            entry = null!;

            if (!_entries.TryGetValue(serverName, out var found)) return false;
            if (found.Client is null || !string.Equals(found.Fingerprint, fingerprint, StringComparison.Ordinal)) return false;
            if (DateTime.UtcNow < found.NextAttemptAt) return false;

            entry = found;
            return true;
        }

        /// <summary>真去建连并列工具。失败记退避后再抛，成功则换上新条目并把失败计数归零</summary>
        private async Task<Entry> ConnectAsync(OtMcpServer server, string fingerprint, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ConnectTimeout);

            McpClient? client = null;
            try
            {
                var transport = CreateTransport(server);

                client = await McpClient.CreateAsync(transport, new McpClientOptions
                {
                    ClientInfo = new Implementation { Name = "viv-ouroboros", Version = "1.0.0" },
                    InitializationTimeout = ConnectTimeout
                }, loggerFactory: null, timeout.Token).ConfigureAwait(false);

                var tools = await client.ListToolsAsync(cancellationToken: timeout.Token).ConfigureAwait(false);

                var byName = new Dictionary<string, McpClientTool>(StringComparer.Ordinal);
                var descriptors = new List<McpToolDescriptor>(tools.Count);
                foreach (var tool in tools)
                {
                    byName[tool.Name] = tool;
                    descriptors.Add(new McpToolDescriptor(tool.Name,
                        tool.Description ?? string.Empty,
                        tool.JsonSchema.ValueKind == JsonValueKind.Undefined ? default : tool.JsonSchema.Clone()));
                }

                var entry = new Entry(server.ServerName, fingerprint)
                {
                    Client = client,
                    Tools = byName,
                    Descriptors = descriptors
                };

                _entries[server.ServerName] = entry;
                _logger.Info("MCP 服务已连接：{0}（{1}，{2} 个工具：{3}）",
                    server.ServerName, Describe(server), descriptors.Count, string.Join("、", descriptors.Select(x => x.Name)));
                return entry;
            }
            catch (Exception ex)
            {
                if (client is not null)
                {
                    try { await client.DisposeAsync().ConfigureAwait(false); }
                    catch { /* 建连失败时释放失败没有进一步可做的 */ }
                }

                // 配置写错（地址/命令行/传输方式）与网络不通在这里是同一形状：都记退避 + 原样上报
                var backoff = RecordFailure(server.ServerName, fingerprint, ex);
                throw new ToolExecutionException(
                    $"连接 MCP 服务 {server.ServerName} 失败（{backoff} 秒内不再重试）：{ex.Message}", ex.Message);
            }
        }

        /// <summary>
        /// 连接被判定为坏了：丢掉连接，留一个带退避的占位条目，让随后的调用快速失败而不是反复去连。
        /// </summary>
        private void Break(string serverName, Exception reason)
        {
            if (!_entries.TryRemove(serverName, out var broken)) return;
            if (broken.Client is not null) Release(broken.Client, serverName);
            RecordFailure(serverName, broken.Fingerprint, reason, broken.Failures);
        }

        /// <summary>记一次失败并算出退避秒数（2/4/8/16/30）。返回本次退避秒数。</summary>
        private int RecordFailure(string serverName, string fingerprint, Exception ex, int previousFailures = 0)
        {
            var failures = previousFailures + 1;
            var seconds = (int)Math.Min(MaxBackoff.TotalSeconds, BaseBackoff.TotalSeconds * Math.Pow(2, failures - 1));

            _entries[serverName] = new Entry(serverName, fingerprint)
            {
                Failures = failures,
                NextAttemptAt = DateTime.UtcNow.AddSeconds(seconds),
                LastError = ex.Message
            };

            _logger.Warning("MCP 服务连接失败（第 {0} 次，{1} 秒内不再重试）：{2}，{3}", failures, seconds, serverName, ex.Message);
            return seconds;
        }

        /// <summary>释放连接：不等结果（失败路径上不能阻塞），异常只记日志</summary>
        private void Release(McpClient client, string label)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await client.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Warning("释放 MCP 连接失败（忽略）：{0}，{1}", label, ex.Message);
                }
            });
        }

        /// <summary>
        /// 配置指纹：改了传输/地址/请求头就必须换连接。含 Headers 原文，所以**只做相等比较、绝不外泄**。
        /// 工具白名单与审批模式不影响连接，改它们由工具装配那层（60 秒缓存 + 版本戳）收敛。
        /// </summary>
        private static string BuildFingerprint(OtMcpServer server)
            => $"{server.Transport}|{server.ServerAddress}|{server.Headers}";

        /// <summary>日志用的传输描述（不含 Headers）</summary>
        private static string Describe(OtMcpServer server)
            => server.Transport == EmMcpTransport.Stdio
                ? $"stdio：{server.ServerAddress}"
                : $"{server.Transport}：{server.ServerAddress}";

        /// <summary>
        /// 按 <see cref="OtMcpServer.Transport"/> 造传输。
        ///
        /// <c>HttpSse</c> 走 SDK 的 <c>AutoDetect</c>：它同时兼容 SSE 与 StreamableHttp，
        /// 而这一列只表达"走 HTTP"，不该为同一列再分模式。
        ///
        /// <c>Stdio</c> 有一处约定：实体没有"命令/参数"列，<c>ServerAddress</c> 的注释又写的是"HTTP/SSE 时"，
        /// 所以这里把 <c>ServerAddress</c> 读作<b>命令行</b>（支持用双引号包住带空格的路径）。
        /// 需要这样约定而不是加列 —— 本任务不允许改表结构。
        /// </summary>
        private IClientTransport CreateTransport(OtMcpServer server)
        {
            switch (server.Transport)
            {
                case EmMcpTransport.HttpSse:
                {
                    var address = server.ServerAddress?.Trim();
                    if (string.IsNullOrWhiteSpace(address) || !Uri.TryCreate(address, UriKind.Absolute, out var endpoint))
                        throw new ToolExecutionException($"MCP 服务的 ServerAddress 不是合法的绝对地址：{server.ServerName}", "ServerAddress 非法");

                    var options = new HttpClientTransportOptions
                    {
                        Name = server.ServerName,
                        Endpoint = endpoint,
                        TransportMode = HttpTransportMode.AutoDetect,
                        ConnectionTimeout = ConnectTimeout
                    };

                    // 两个集合属性在 SDK 里是可空的（默认可能没实例化），显式兜一下
                    options.AdditionalHeaders ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var header in ParseHeaders(server.Headers, server.ServerName))
                        options.AdditionalHeaders[header.Key] = header.Value;

                    return new HttpClientTransport(options);
                }

                case EmMcpTransport.Stdio:
                {
                    if (!string.IsNullOrWhiteSpace(server.Headers))
                        _logger.Warning("stdio 传输用不上 Headers，该列被忽略：{0}", server.ServerName);

                    var parts = SplitCommandLine(server.ServerAddress, server.ServerName);
                    var options = new StdioClientTransportOptions
                    {
                        Name = server.ServerName,
                        Command = parts[0],
                        // 子进程按惯例继承环境变量：不继承的话 PATH/临时目录这类基本设定也没了
                        InheritEnvironmentVariables = true
                    };

                    options.Arguments ??= new List<string>();

                    for (var i = 1; i < parts.Count; i++) options.Arguments.Add(parts[i]);

                    return new StdioClientTransport(options);
                }

                default:
                    throw new ToolExecutionException(
                        $"MCP 服务的传输方式暂不支持：{server.ServerName}（Transport={server.Transport}）", $"Transport={server.Transport}");
            }
        }

        /// <summary>请求头列的解析：坏 JSON 只记 Warning（不回显原文，那列按注释是放令牌的地方）</summary>
        private Dictionary<string, string> ParseHeaders(string? json, string serverName)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json)) return headers;

            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    _logger.Warning("MCP 服务的 Headers 不是 JSON 对象，已忽略：{0}", serverName);
                    return headers;
                }

                foreach (var property in document.RootElement.EnumerateObject())
                    headers[property.Name] = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.GetRawText();
            }
            catch (JsonException)
            {
                _logger.Warning("MCP 服务的 Headers 不是合法 JSON，已忽略：{0}", serverName);
            }

            return headers;
        }

        /// <summary>把命令行切成"可执行文件 + 参数"，双引号内的空白不当分隔符</summary>
        private static List<string> SplitCommandLine(string? commandLine, string serverName)
        {
            var parts = new List<string>();
            if (string.IsNullOrWhiteSpace(commandLine))
                throw new ToolExecutionException(
                    $"MCP 服务是 stdio 传输，但 ServerAddress（约定为命令行）为空：{serverName}", "ServerAddress 为空");

            var current = new StringBuilder();
            var quoted = false;

            foreach (var ch in commandLine)
            {
                if (ch == '"')
                {
                    quoted = !quoted;
                    continue;
                }

                if (!quoted && char.IsWhiteSpace(ch))
                {
                    if (current.Length > 0)
                    {
                        parts.Add(current.ToString());
                        current.Clear();
                    }

                    continue;
                }

                current.Append(ch);
            }

            if (current.Length > 0) parts.Add(current.ToString());
            if (parts.Count == 0)
                throw new ToolExecutionException($"MCP 服务的命令行解析不出可执行文件：{serverName}", "命令行非法");

            return parts;
        }

        /// <summary>
        /// 把 MCP 的结果摊成给模型的正文。文本块直接取文本，其它块（图片/音频/内嵌资源）退回 SDK 的序列化形式；
        /// 文本块为空但带了 structuredContent 时用后者，免得模型看到一片空白。
        /// </summary>
        private static string ExtractText(CallToolResult result)
        {
            var parts = new List<string>();

            if (result.Content is not null)
            {
                foreach (var block in result.Content)
                    parts.Add(block is TextContentBlock text ? text.Text : block?.ToString() ?? string.Empty);
            }

            if (parts.Count == 0 && result.StructuredContent is { } structured && structured.ValueKind != JsonValueKind.Undefined)
                parts.Add(structured.GetRawText());

            return string.Join("\n", parts.Where(x => !string.IsNullOrEmpty(x)));
        }

        /// <summary>
        /// 一个服务的连接条目。可能是"已连上"（<see cref="Client"/> 非空），
        /// 也可能是"刚失败、正在退避"的占位条目（只有失败记账）。
        /// </summary>
        private sealed class Entry
        {
            public Entry(string label, string fingerprint)
            {
                Label = label;
                Fingerprint = fingerprint;
            }

            /// <summary>服务名（日志用，不含任何凭据）</summary>
            public string Label { get; }

            /// <summary>建连时的配置指纹（只做相等比较）</summary>
            public string Fingerprint { get; }

            /// <summary>活的 MCP 客户端；占位条目为 null</summary>
            public McpClient? Client { get; init; }

            /// <summary>服务侧工具名 → SDK 工具对象（调用时按原名找）</summary>
            public IReadOnlyDictionary<string, McpClientTool> Tools { get; init; } =
                new Dictionary<string, McpClientTool>(StringComparer.Ordinal);

            /// <summary>给装配期用的轻量描述</summary>
            public IReadOnlyList<McpToolDescriptor> Descriptors { get; init; } = [];

            /// <summary>连续失败次数（成功一次即归零，因为成功会换上新条目）</summary>
            public int Failures { get; init; }

            /// <summary>这个时间点之前不再尝试连接</summary>
            public DateTime NextAttemptAt { get; init; } = DateTime.MinValue;

            /// <summary>最近一次失败原因（透传给运维，不含请求头）</summary>
            public string? LastError { get; init; }
        }
    }
}
