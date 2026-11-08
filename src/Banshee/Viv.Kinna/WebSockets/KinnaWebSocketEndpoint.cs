using System.Buffers;
using System.Net.WebSockets;
using System.Threading.Channels;
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

        /// <summary>
        /// 同时在途的心跳上限。心跳本该几十秒一次，能堆到这么多说明客户端在刷或者连接已经不对了。
        /// 有上限才不会让一条连接在服务端堆出无限任务。
        /// </summary>
        private const int MaxInflightPings = 4;

        /// <summary>
        /// 有序指令（Ack / Resync）的待处理队列深度。满了读循环就停下来等，
        /// 把压力交回 TCP 窗口——比在服务端堆帧好，比让客户端「连上了但什么都发不出去」也好。
        /// </summary>
        private const int SerialQueueCapacity = 64;

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

            // 身份空判定必须在准入之前：默认准入对 null 身份也是拒绝，
            // 放在后面会让 no_identity 这个拒绝原因永远走不到，指标上永远读成 admission
            if (identity == null)
            {
                KinnaMetrics.RecordWebSocketRejected(HubName, KinnaWebSocketRejectReasons.NoIdentity);
                http.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

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

            // 子协议在 101 之前谈定：客户端要的是 kinna.v2 而服务端只会 v1 时，这里直接 400，
            // 比让它连上之后按错的格式发帧、被当成脏帧悄悄丢掉好排查
            var requestedProtocols = http.WebSockets.WebSocketRequestedProtocols;
            var protocol = NegotiateSubProtocol(requestedProtocols);

            if (protocol == null && requestedProtocols.Count > 0)
            {
                KinnaMetrics.RecordWebSocketRejected(HubName, KinnaWebSocketRejectReasons.SubProtocol);
                logger.Warning($"原生 WebSocket 子协议不支持: {string.Join(",", requestedProtocols)}");
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var socket = await http.WebSockets.AcceptWebSocketAsync(protocol).ConfigureAwait(false);

            var connectionId = KinnaConnectionIds.NewWebSocket();
            registry.Bind(connectionId, socket, http.RequestAborted);

            // 从 Bind 开始这条连接就挂在 registry 上了：注册、握手、读循环任何一步抛异常，
            // 都得由 finally 摘掉。以前 RegisterAsync 抛（比如查群失败）时 finally 还没生效，
            // 会留下一条 socket 已死、表里还在的僵尸连接，随连接尝试次数线性泄漏。
            var closedByUs = false;

            // 关闭帧的描述要能反映真实原因：读循环自己退出时都先发过关闭帧（见 ReadLoopAsync），
            // 能走到 finally 的兜底分支，说明连接是被外部打断的（请求中止、注册抛异常）
            var registered = false;

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

                registered = true;

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
                // 读循环自己退出时都已经发过关闭帧（客户端关闭、帧过大、连接失效），
                // 还能走到这儿且 socket 仍开着，说明是被外部打断的。描述按真实原因给，
                // 别一律写 "register failed" —— 客户端和排障的人会被这四个字带偏。
                // 顺序不能反：Unbind 会 abort socket，之后再写关闭帧已经写不出去了
                if (!closedByUs && socket.State == WebSocketState.Open)
                {
                    await registry
                        .CloseOutputAsync(
                            connectionId,
                            WebSocketCloseStatus.EndpointUnavailable,
                            registered ? "connection aborted" : "register failed")
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

        /// <summary>
        /// 协商子协议。客户端一个都没声明时返回 null —— 老客户端（小程序早期版本）不传这个头，
        /// 按 v1 处理，不能用「没声明」去拒绝它们；声明了但一个都不认识也返回 null，由调用方按拒绝处理。
        /// </summary>
        private static string? NegotiateSubProtocol(IList<string> requested)
        {
            foreach (var protocol in requested)
            {
                if (string.Equals(protocol, KinnaWebSocketProtocols.V1, StringComparison.OrdinalIgnoreCase))
                {
                    return KinnaWebSocketProtocols.V1;
                }
            }

            return null;
        }

        /// <summary>
        /// 收上行帧。这里只做「读 + 解码 + 分流」，不等着指令跑完：
        /// 有序指令（Ack / Resync）丢给 <c>serial</c> 队列由单独的派发任务串行消费，
        /// 心跳直接旁路。以前这里是 <c>await HandleMessageAsync(...)</c>，一条 Ack 卡在存储上，
        /// 后面的帧连读都读不出来——客户端收不到 Pong 就以为掉线，重连风暴把存储压得更死。
        /// </summary>
        private static async Task ReadLoopAsync(
            HttpContext http,
            WebSocket socket,
            string connectionId,
            KinnaWebSocketRegistry<THub> registry,
            IServiceScopeFactory scopes,
            KinnaOptions options,
            ILoggerContract logger)
        {
            var requestAborted = http.RequestAborted;
            var maxMessageBytes = options.WebSocketMaxMessageBytes > 0
                ? options.WebSocketMaxMessageBytes
                : 32 * 1024;

            // 旁路心跳和有序列派发只在这一个条件下取消它：判定连接已失效。
            // 关闭帧仍然由读循环发，一条连接只有一个收尾方，帧的顺序才可控
            using var expired = new CancellationTokenSource();
            using var receiveLifetime = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, expired.Token);
            var receiveToken = receiveLifetime.Token;

            // 在途心跳数。心跳该是几十秒一次，堆到上限说明客户端在刷或者卡住了，
            // 丢弃比继续建任务好——它下一次心跳还会来
            var inflightPings = 0;

            // 有序指令走这条队列。读循环不再 await 它们，只负责尽快把帧读出来；
            // 顺序由单读者保证。队列满时 WriteAsync 会等，读循环自然停下——这就是背压，
            // 比无限堆内存好，也比让客户端以为连接正常却什么都收不到好
            var serial = Channel.CreateBounded<KinnaClientMessage>(new BoundedChannelOptions(SerialQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false
            });

            var serialLoop = RunSerialAsync();

            var buffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
            using var message = new MemoryStream();

            try
            {
                while (!receiveToken.IsCancellationRequested)
                {
                    if (expired.IsCancellationRequested)
                    {
                        await CloseExpiredAsync().ConfigureAwait(false);
                        return;
                    }

                    ValueWebSocketReceiveResult result;

                    try
                    {
                        result = await socket.ReceiveAsync(buffer.AsMemory(), receiveToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (expired.IsCancellationRequested)
                    {
                        // 心跳或串行派发判定服务端已经不认这条连接，关闭帧还没发，这里补上
                        await CloseExpiredAsync().ConfigureAwait(false);
                        return;
                    }

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

                    var parsed = KinnaWebSocketProtocol.Parse(payload);

                    if (parsed?.Type == null)
                    {
                        // 不认识就不认，不断连接：以后加指令时老客户端不该因此被踢
                        logger.Debug($"原生 WebSocket 收到无法解析的帧，已忽略: {connectionId}");
                        continue;
                    }

                    // 心跳不排队：Pong 只读一次连接表，没有顺序依赖，不该为一条慢 Ack 陪葬
                    if (parsed.Type == KinnaInboundMethods.Ping)
                    {
                        if (Interlocked.Increment(ref inflightPings) > MaxInflightPings)
                        {
                            Interlocked.Decrement(ref inflightPings);
                            logger.Debug($"原生 WebSocket 在途心跳已达上限，丢弃本次: {connectionId}");
                            continue;
                        }

                        _ = DispatchPingAsync(parsed);
                        continue;
                    }

                    try
                    {
                        await serial.Writer.WriteAsync(parsed, receiveToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (expired.IsCancellationRequested)
                    {
                        await CloseExpiredAsync().ConfigureAwait(false);
                        return;
                    }
                }
            }
            finally
            {
                serial.Writer.TryComplete();

                // 队列里可能还有没跑完的指令（客户端确认完就断线）：等它排空再收尾，
                // 否则那些确认会静默丢掉。派发卡死和以前一样会拖住请求，不额外加时限
                await serialLoop.ConfigureAwait(false);

                ArrayPool<byte>.Shared.Return(buffer);
            }

            // 收尾：先记指标再发关闭帧。三条失效路径共用，保证只发一次、顺序固定
            async Task CloseExpiredAsync()
            {
                KinnaMetrics.RecordWebSocketClosed(HubName, KinnaWebSocketCloseReasons.Expired);
                logger.Warning($"原生 WebSocket 连接已失效，主动断开: {connectionId}");

                await registry
                    .CloseOutputAsync(connectionId, WebSocketCloseStatus.PolicyViolation, "connection expired")
                    .ConfigureAwait(false);
            }

            // 判定失效后只负责通知读循环，关闭由读循环统一做：
            // 两边都关的话会发两次关闭帧，第二帧还会把 socket 状态弄乱
            async Task NotifyExpiredAsync()
            {
                try
                {
                    await expired.CancelAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    // 连接已经收尾（读循环先退出、CTS 已释放），没有要通知的人
                }
            }

            // 有序指令的单读者：Ack / Resync 之间保持先后，但不和读循环抢时间
            async Task RunSerialAsync()
            {
                try
                {
                    while (await serial.Reader.WaitToReadAsync(receiveToken).ConfigureAwait(false))
                    {
                        while (serial.Reader.TryRead(out var queued))
                        {
                            if (await DispatchOnceAsync(queued, connectionId, registry, scopes, logger, receiveToken)
                                    .ConfigureAwait(false))
                            {
                                await NotifyExpiredAsync().ConfigureAwait(false);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // 连接已中止（请求中断或判定失效），队列里剩的不再处理
                }
                catch (Exception ex)
                {
                    logger.Error($"原生 WebSocket 有序指令派发失败: {connectionId}", ex);
                }
            }

            // 旁路的心跳：只管把 Pong 发出去。没人 await 它，异常必须在这里收掉
            async Task DispatchPingAsync(KinnaClientMessage incoming)
            {
                try
                {
                    if (await DispatchOnceAsync(incoming, connectionId, registry, scopes, logger, requestAborted)
                            .ConfigureAwait(false))
                    {
                        await NotifyExpiredAsync().ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (requestAborted.IsCancellationRequested)
                {
                    // 请求中止了，Pong 发不出去是正常的，别记成错误
                }
                catch (ObjectDisposedException)
                {
                    // 连接已经收尾，没有要通知的人
                }
                catch (Exception ex)
                {
                    logger.Error($"原生 WebSocket 心跳处理失败: {connectionId}", ex);
                }
                finally
                {
                    Interlocked.Decrement(ref inflightPings);
                }
            }
        }

        /// <summary>
        /// 处理一条上行指令，返回 true 表示这条连接应当关掉。
        ///
        /// 串行路径（Ack / Resync）和旁路（Ping）共用：协议翻译只有一份，
        /// 两条路的差别只在「要不要排队」，不在语义。
        /// </summary>
        private static async Task<bool> DispatchOnceAsync(
            KinnaClientMessage message,
            string connectionId,
            KinnaWebSocketRegistry<THub> registry,
            IServiceScopeFactory scopes,
            ILoggerContract logger,
            CancellationToken cancellationToken)
        {
            KinnaOutbound outbound;

            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var coordinator = scope.ServiceProvider.GetRequiredService<IKinnaConnectionCoordinator<THub>>();
                outbound = await KinnaWebSocketProtocol
                    .DispatchAsync(message, coordinator, connectionId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 连接已中止，交给读循环的收尾路径
                throw;
            }
            catch (Exception ex)
            {
                // 一条指令处理失败不该拆掉整条连接：动作已经在 101 之后，抛出去只会让 Kestrel
                // 直接 abort，客户端连「为什么」都拿不到。不回帧即可 —— 客户端会按自己的节奏重试，
                // 而 Ack 这类关键指令内部已经降级成 Failed 了，走到这里的是真意外。
                logger.Error($"原生 WebSocket 上行指令处理失败，已忽略本帧: {connectionId} type={message.Type}", ex);
                return false;
            }

            if (outbound.Method != null)
            {
                registry.Send(connectionId, outbound.Method, [outbound.Payload]);
            }

            return outbound.Close;
        }
    }
}
