using System.Buffers;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Viv.Kinna.Connections;
using Viv.Kinna.Contracts;
using Viv.Kinna.Metrics;
using Viv.Kinna.Options;
using Viv.Kinna.Ports;
using Viv.Log;

namespace Viv.Kinna.WebSockets
{
    /// <summary>
    /// 原生 WebSocket 端点。做的事和 <c>ChatHub.OnConnectedAsync</c> 完全一样：
    /// 取身份 → 准入 → 登记 → 回握手帧 → 读循环 → 注销。
    ///
    /// 协调器只依赖连接表和发送器，不碰 <c>Hub.Context</c>，所以两套传输共用同一份协议逻辑，
    /// 差别只在这层壳：这边没有 RPC 语义，一行上行帧对应一行下行帧。
    ///
    /// 协调器是 Scoped，每条消息单独开作用域——长连接如果把 Scoped 服务一直攥在手里，
    /// 背后的 DbContext / 连接会跟着连接活几个小时。
    /// </summary>
    internal static class KinnaWebSocketEndpoint<THub> where THub : Hub
    {
        private const int ReceiveBufferSize = 4 * 1024;

        /// <summary>模块名，只用于指标标签</summary>
        private static readonly string HubName = typeof(THub).Name;

        internal static async Task HandleAsync(HttpContext http)
        {
            var services = http.RequestServices;
            var logger = services.GetRequiredService<ILoggerContract>();

            if (!http.WebSockets.IsWebSocketRequest)
            {
                KinnaMetrics.RecordWebSocketRejected(HubName, KinnaWebSocketRejectReasons.NotUpgrade);
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var resolver = services.GetService<IKinnaIdentityResolver<THub>>();
            if (resolver == null)
            {
                // 缺这层就等于没有鉴权，宁可不接也不能放行
                KinnaMetrics.RecordWebSocketRejected(HubName, KinnaWebSocketRejectReasons.NoResolver);
                logger.Error($"未注册 IKinnaIdentityResolver<{typeof(THub).Name}>，原生 WebSocket 端点已拒绝所有连接");
                http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }

            var registry = services.GetRequiredService<KinnaWebSocketRegistry<THub>>();
            var session = services.GetRequiredService<IKinnaSession<THub>>();
            var scopes = services.GetRequiredService<IServiceScopeFactory>();

            var identity = await resolver.ResolveAsync(http).ConfigureAwait(false);

            await using (var scope = scopes.CreateAsyncScope())
            {
                var admission = scope.ServiceProvider.GetRequiredService<IKinnaAdmission<THub>>();
                var check = await admission.CheckAsync(identity, http.RequestAborted).ConfigureAwait(false);

                if (!check.Allowed)
                {
                    // Upgrade 之前还能用 HTTP 状态码，客户端 fetch/wx.connectSocket 都能读到；
                    // 一旦 Accept 了，就只剩 200 + 关闭帧，看不出原因
                    KinnaMetrics.RecordWebSocketRejected(HubName, KinnaWebSocketRejectReasons.Admission);
                    logger.Warning($"拒绝原生 WebSocket 连接: {check.Reason}");
                    http.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
            }

            if (identity == null)
            {
                KinnaMetrics.RecordWebSocketRejected(HubName, KinnaWebSocketRejectReasons.NoIdentity);
                http.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            using var socket = await http.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);

            var connectionId = KinnaConnectionIds.NewWebSocket();
            registry.Bind(connectionId, socket, http.RequestAborted);

            // 从 Bind 开始这条连接就挂在 registry 上了：注册、握手、读循环任何一步抛异常，
            // 都得由 finally 摘掉。以前 RegisterAsync 抛（比如查群失败）时 finally 还没生效，
            // 会留下一条 socket 已死、表里还在的僵尸连接，随连接尝试次数线性泄漏。
            var closedByUs = false;

            try
            {
                KinnaConnected? connected;

                await using (var scope = scopes.CreateAsyncScope())
                {
                    var coordinator = scope.ServiceProvider.GetRequiredService<IKinnaConnectionCoordinator<THub>>();
                    connected = await coordinator
                        .RegisterAsync(connectionId, identity, http.RequestAborted)
                        .ConfigureAwait(false);
                }

                if (connected == null)
                {
                    closedByUs = true;
                    KinnaMetrics.RecordWebSocketRejected(HubName, KinnaWebSocketRejectReasons.RegisterFailed);

                    await registry
                        .CloseOutputAsync(connectionId, WebSocketCloseStatus.PolicyViolation, "denied")
                        .ConfigureAwait(false);

                    return;
                }

                registry.Send(connectionId, KinnaClientMethods.Connected, [connected]);

                try
                {
                    await ReadLoopAsync(http, socket, connectionId, registry, scopes, session.Options, logger)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // 请求中止：客户端刷新、反代超时、进程停机
                }
                catch (WebSocketException)
                {
                    // 对端 RST，没走关闭握手
                }
            }
            finally
            {
                // 握手没走完就被打断：补一个关闭帧，客户端能立刻重连，不用等 TCP 超时。
                // 顺序不能反——Unbind 会 abort socket，之后再写关闭帧已经写不出去了
                if (!closedByUs && socket.State == WebSocketState.Open)
                {
                    await registry
                        .CloseOutputAsync(connectionId, WebSocketCloseStatus.EndpointUnavailable, "register failed")
                        .ConfigureAwait(false);
                }

                // 先摘传输再注销身份：反过来的话，中间进来的推送还会往一条已经关掉的 socket 上写
                registry.Unbind(connectionId);

                await UnregisterSafelyAsync(scopes, connectionId, logger).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 收尾注销。这一段在 finally 里，异常抛出去会盖掉真正的失败原因，所以自己吞掉并记日志。
        /// </summary>
        private static async Task UnregisterSafelyAsync(
            IServiceScopeFactory scopes,
            string connectionId,
            ILoggerContract logger)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var coordinator = scope.ServiceProvider.GetRequiredService<IKinnaConnectionCoordinator<THub>>();
                await coordinator.UnregisterAsync(connectionId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Error($"原生 WebSocket 连接 {connectionId} 注销失败", ex);
            }
        }

        private static async Task ReadLoopAsync(
            HttpContext http,
            WebSocket socket,
            string connectionId,
            KinnaWebSocketRegistry<THub> registry,
            IServiceScopeFactory scopes,
            KinnaOptions options,
            ILoggerContract logger)
        {
            var cancellationToken = http.RequestAborted;
            var maxMessageBytes = options.WebSocketMaxMessageBytes > 0
                ? options.WebSocketMaxMessageBytes
                : 32 * 1024;

            var buffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
            using var message = new MemoryStream();

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        KinnaMetrics.RecordWebSocketClosed(HubName, KinnaWebSocketCloseReasons.ClientClosed);

                        // 回一个关闭帧再走，客户端才不会当成异常断开。
                        // 走写循环而不是直接写 socket：这里可能还有下行帧在途
                        await registry
                            .CloseOutputAsync(connectionId, WebSocketCloseStatus.NormalClosure, null)
                            .ConfigureAwait(false);

                        return;
                    }

                    // 上行只有 JSON 文本；二进制帧没人会发，收到就当它不存在
                    if (result.MessageType != WebSocketMessageType.Text || result.Count == 0)
                    {
                        continue;
                    }

                    if (message.Length + result.Count > maxMessageBytes)
                    {
                        KinnaMetrics.RecordWebSocketClosed(HubName, KinnaWebSocketCloseReasons.FrameTooLarge);
                        logger.Warning($"原生 WebSocket 上行帧超过 {maxMessageBytes} 字节，断开连接: {connectionId}");

                        await registry
                            .CloseOutputAsync(connectionId, WebSocketCloseStatus.MessageTooBig, "frame too large")
                            .ConfigureAwait(false);

                        return;
                    }

                    message.Write(buffer, 0, result.Count);

                    // 分片：一条逻辑消息可能分几帧到，凑齐 EndOfMessage 再解析
                    if (!result.EndOfMessage)
                    {
                        continue;
                    }

                    var payload = message.ToArray();
                    message.SetLength(0);

                    var close = await HandleMessageAsync(payload, connectionId, registry, scopes, logger, cancellationToken)
                        .ConfigureAwait(false);

                    if (close)
                    {
                        // 服务端已经不认这条连接（租约被清扫、被别的副本踢掉）：
                        // 应答帧已经入队，关闭帧排在它后面，客户端能先看到原因再断
                        KinnaMetrics.RecordWebSocketClosed(HubName, KinnaWebSocketCloseReasons.Expired);
                        logger.Warning($"原生 WebSocket 连接已失效，主动断开: {connectionId}");

                        await registry
                            .CloseOutputAsync(connectionId, WebSocketCloseStatus.PolicyViolation, "connection expired")
                            .ConfigureAwait(false);

                        return;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>
        /// 处理一条上行帧，返回 true 表示这条连接应当关掉。
        /// </summary>
        private static async Task<bool> HandleMessageAsync(
            byte[] payload,
            string connectionId,
            KinnaWebSocketRegistry<THub> registry,
            IServiceScopeFactory scopes,
            ILoggerContract logger,
            CancellationToken cancellationToken)
        {
            var message = KinnaWebSocketProtocol.Parse(payload);

            if (message?.Type == null)
            {
                // 不认识就不认，不断连接：以后加指令时老客户端不该因此被踢
                logger.Debug($"原生 WebSocket 收到无法解析的帧，已忽略: {connectionId}");
                return false;
            }

            KinnaOutbound outbound;

            await using (var scope = scopes.CreateAsyncScope())
            {
                var coordinator = scope.ServiceProvider.GetRequiredService<IKinnaConnectionCoordinator<THub>>();
                outbound = await KinnaWebSocketProtocol
                    .DispatchAsync(message, coordinator, connectionId, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (outbound.Method != null)
            {
                registry.Send(connectionId, outbound.Method, [outbound.Payload]);
            }

            return outbound.Close;
        }
    }
}
