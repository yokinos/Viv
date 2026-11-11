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
        /// 有序指令（Ack / Resync）队列深度的兜底值。正常取
        /// <see cref="KinnaOptions.WebSocketSerialQueueCapacity"/>，配 0 或负数时才用它。
        /// 队列满了就丢弃新来的那条，读循环绝不停下——停下的代价是心跳跟着堵死，
        /// 客户端判定掉线重连，重连风暴把存储压得更死。
        /// 丢的是「回执」不是「消息」：Ack 丢弃时回 Throttled 让客户端稍后重报，水位不推进会重发，
        /// Resync 丢弃时回 Throttled 让客户端退避重试，两者都不碰存储。
        /// </summary>
        private const int DefaultSerialQueueCapacity = 64;

        /// <summary>
        /// 服务端主动发关闭帧之后，等对端回一帧 Close 完成握手的上限。
        /// 直接 Unbind→Abort 会发 RST，内核把对端还没读走的关闭帧一起冲掉，
        /// 客户端只看到 1006 / "without completing the close handshake"，拿不到关闭原因。
        /// 本地回环下几十毫秒足够；对端不配合时按上限正常收尾，不无限等。
        /// </summary>
        private static readonly TimeSpan CloseHandshakeGrace = TimeSpan.FromMilliseconds(200);

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

            // 限流回帧里的 ServerTime 要走同一个时间源：测试推进 FakeTimeProvider 时，
            // 客户端看到的服务端时间不该是真实的墙上时间
            var time = services.GetService<TimeProvider>() ?? TimeProvider.System;

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

            // 子协议在 101 之前谈定：客户端声明的是本服务端不认识的版本（比如将来的 kinna.v2）时，
            // 这里直接 400 —— 比让它连上之后按错格式发帧、被当成脏帧悄悄丢掉好排查
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

            // 发关闭帧并等对端回一帧 Close 完成握手（有上限）。
            //
            // 只发不等不行：紧跟着的 Unbind→Abort 会发 RST，把对端还没读走的关闭帧一起冲掉，
            // 客户端只看到 1006 / "without completing the close handshake"，拿不到
            // 「denied」「register failed」这些原因。读循环自己收尾的那几条路径在
            // CloseExpiredAsync 里做同一件事，这里是读循环没跑起来（注册被拒）或
            // 被外部打断时的那一条。对端不配合就按上限收尾，不等死。
            async Task CloseAndWaitAsync(WebSocketCloseStatus status, string description)
            {
                // 帧压根没写出去时再等一个不会来的回帧没有意义
                if (!await registry.CloseOutputAsync(connectionId, status, description).ConfigureAwait(false))
                {
                    return;
                }

                var scratch = new byte[64];
                Task<ValueWebSocketReceiveResult>? reply = null;

                try
                {
                    reply = socket.ReceiveAsync(scratch.AsMemory(), http.RequestAborted).AsTask();
                    await reply.WaitAsync(CloseHandshakeGrace, time).ConfigureAwait(false);
                }
                catch (TimeoutException) { }
                catch (OperationCanceledException) { }
                catch (WebSocketException) { }
                catch (ObjectDisposedException) { }
                finally
                {
                    // 超时或异常时这次读可能仍挂在 socket 上，它的异常得有人观测
                    if (reply is { IsCompleted: false } pending)
                    {
                        _ = pending.ContinueWith(
                            static t =>
                            {
                                if (t.IsFaulted) _ = t.Exception;
                            },
                            CancellationToken.None,
                            TaskContinuationOptions.ExecuteSynchronously,
                            TaskScheduler.Default);
                    }
                }
            }

            try
            {
                KinnaConnected? connected;

                await using (var scope = scopes.CreateAsyncScope())
                {
                    var coordinator = scope.ServiceProvider.GetRequiredService<IKinnaConnectionCoordinator<THub>>();

                    // 握手帧由协调器在「加组之前」回调出来：加组一完成群推送就可能到达，
                    // 等 RegisterAsync 返回再发 Connected，客户端会先收到业务帧再收到握手回执
                    connected = await coordinator
                        .RegisterAsync(
                            connectionId,
                            identity,
                            http.RequestAborted,
                            frame =>
                            {
                                registry.Send(connectionId, KinnaClientMethods.Connected, [frame]);
                                return Task.CompletedTask;
                            })
                        .ConfigureAwait(false);
                }

                if (connected == null)
                {
                    closedByUs = true;
                    KinnaMetrics.RecordWebSocketRejected(HubName, KinnaWebSocketRejectReasons.RegisterFailed);

                    await CloseAndWaitAsync(WebSocketCloseStatus.PolicyViolation, "denied").ConfigureAwait(false);

                    return;
                }

                registered = true;

                try
                {
                    await ReadLoopAsync(http, socket, connectionId, registry, scopes, session.Options, time, logger)
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
                //
                // IsClosing 那道门不能省：对端不读时 SettleAsync 里的关闭帧会等满 CloseFrame
                // 才失败，而失败之后 socket.State 仍是 Open（帧压根没写出去）——只看状态就会
                // 再关一次、再等一遍，单连接 10 秒才收尾，close_timeout 还翻倍。
                // MarkClosing 是 SettleAsync 第一行做的，所以这里能准确判断「已经收过尾」
                //
                // 顺序不能反：Unbind 会 abort socket，之后再写关闭帧已经写不出去了
                if (!closedByUs
                    && !registry.IsClosing(connectionId)
                    && socket.State == WebSocketState.Open)
                {
                    await CloseAndWaitAsync(
                        WebSocketCloseStatus.EndpointUnavailable,
                        registered ? "connection aborted" : "register failed").ConfigureAwait(false);
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
        /// 协商子协议，只认 <see cref="KinnaWebSocketProtocols.V1"/>（目前也只有这一个版本）。
        /// 客户端没声明时返回 null：没有版本信息就按 v1 处理（默认值）；
        /// 声明了但一个都不认识也返回 null，由调用方按拒绝处理。
        /// </summary>
        private static string? NegotiateSubProtocol(IList<string> requested)
        {
            foreach (var protocol in requested)
            {
                if (string.Equals(protocol, KinnaWebSocketProtocols.V1, StringComparison.OrdinalIgnoreCase))
                {
                    // 回客户端声明的原串，不回常量：子协议 token 大小写敏感，
                    // 客户端写 Kinna.V1 而我们回 kinna.v1 时，严格实现会拒绝这次握手
                    return protocol;
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
            TimeProvider time,
            ILoggerContract logger)
        {
            var requestAborted = http.RequestAborted;
            var maxMessageBytes = options.WebSocketMaxMessageBytes > 0
                ? options.WebSocketMaxMessageBytes
                : 32 * 1024;

            // 旁路心跳和有序列派发只在这一个条件下通知失效；关闭帧仍然由读循环发，
            // 一条连接只有一个收尾方，帧的顺序才可控。
            //
            // 两个令牌必须分开：expired 只用来打断在途指令（dispatchToken），绝不能拿去取消
            // socket.ReceiveAsync —— .NET 的 WebSocket 取消一次读之后会把整个 socket 打成
            // Aborted，之后 CloseOutputAsync 再也写不出关闭帧，客户端收到 Pong 后只看到 1006，
            // 拿不到「connection expired」这个原因。读循环改用 expiredSignal 这个 Task 等失效。
            using var expired = new CancellationTokenSource();
            var expiredSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var receiveToken = requestAborted;
            using var dispatchLifetime = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, expired.Token);
            var dispatchToken = dispatchLifetime.Token;

            // 在途心跳数。心跳该是几十秒一次，堆到上限说明客户端在刷或者卡住了，
            // 丢弃比继续建任务好——它下一次心跳还会来
            var inflightPings = 0;

            // 有序指令走这条队列。读循环不再 await 它们，只负责尽快把帧读出来；
            // 顺序由单读者保证。队列满时用 TryWrite 直接失败（这一模式不会挂起调用方），
            // 由调用方回一帧「限流」了事 —— 读循环一旦停下来等，
            // 排队在后面的心跳就发不出去，客户端判定掉线重连，越忙越断
            var serialCapacity = options.WebSocketSerialQueueCapacity > 0
                ? options.WebSocketSerialQueueCapacity
                : DefaultSerialQueueCapacity;

            var serial = Channel.CreateBounded<KinnaClientMessage>(new BoundedChannelOptions(serialCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false
            });

            var serialLoop = RunSerialAsync();

            var buffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
            using var message = new MemoryStream();

            // 收尾只做一次：客户端关闭、帧过大、判定失效三条路径加上 finally 兜底都会走到这儿。
            // 重复排空队列无害，重复发关闭帧不是
            var settled = 0;

            // 失效信号抢在读前面时，那次读还挂在 socket 上。留着它等客户端的 Close 回帧，
            // 别在同一条 socket 上再发起第二个读
            Task<ValueWebSocketReceiveResult>? pendingReceive = null;

            try
            {
                while (true)
                {
                    // 先判「服务端已经不认这条连接」。失效由 WhenAny 唤醒，或在下一轮这里命中标志，
                    // 两条路都走 CloseExpiredAsync。以前这一步走不到，失效连接会静默退出：
                    // 不发关闭帧、也不记 expired
                    if (expired.IsCancellationRequested)
                    {
                        await CloseExpiredAsync().ConfigureAwait(false);
                        return;
                    }

                    if (receiveToken.IsCancellationRequested)
                    {
                        // 只可能是 requestAborted：请求已经在收尾，关闭帧发不出去也没必要发
                        return;
                    }

                    ValueWebSocketReceiveResult result;

                    // 等「读到一帧」和「判定失效」谁先到。不能靠取消 ReceiveAsync 来叫醒读循环：
                    // 取消一次 WebSocket 读会把 socket 打成 Aborted，随后的关闭帧就再也写不出去。
                    // 失效先到时那次读还挂在 socket 上，交给 CloseExpiredAsync 复用去等回帧，
                    // 不在同一条 socket 上再发起第二个读。
                    var receive = socket.ReceiveAsync(buffer.AsMemory(), receiveToken).AsTask();
                    var winner = await Task.WhenAny(receive, expiredSignal.Task).ConfigureAwait(false);
                    if (winner != receive)
                    {
                        pendingReceive = receive;
                        await CloseExpiredAsync().ConfigureAwait(false);
                        return;
                    }

                    try
                    {
                        result = await receive.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // 请求中止（客户端刷新、反代超时、进程停机）
                        return;
                    }

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        // 表里还有它 = 这一次确实是客户端先关的。被踢（Terminate）、停机（AbortAll）
                        // 都已经先把它摘表并记过原因了，这里再记一条 client_closed 会把停机/踢人
                        // 的计数翻倍，看板上就成了「每次发布都有一半是客户端主动关」
                        if (registry.IsLocal(connectionId))
                        {
                            KinnaMetrics.RecordWebSocketClosed(HubName, KinnaWebSocketCloseReasons.ClientClosed);
                        }

                        // 回一个关闭帧再走，客户端才不会当成异常断开。
                        // 走写循环而不是直接写 socket：这里可能还有下行帧在途
                        await SettleAsync(WebSocketCloseStatus.NormalClosure, null).ConfigureAwait(false);

                        return;
                    }

                    // 上行只有 JSON 文本；二进制帧没人会发，收到就当它不存在。
                    // 但它可能夹在分片中间：这时必须把已经拼的半截文本丢掉，
                    // 否则下一条消息会粘上残留（`{"type":"pi` + `{"type":"ping"}`），
                    // 解析不出来被当成脏帧忽略——表现为「客户端发什么都没反应」，极难查。
                    // 空帧不能在这里拦：空帧作为分片的结束帧是合法的，要让它走完收尾流程，
                    // 否则同样会留下残留（上一版就是这里 continue 掉的）
                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        message.SetLength(0);
                        continue;
                    }

                    if (message.Length + result.Count > maxMessageBytes)
                    {
                        KinnaMetrics.RecordWebSocketClosed(HubName, KinnaWebSocketCloseReasons.FrameTooLarge);
                        logger.Warning($"原生 WebSocket 上行帧超过 {maxMessageBytes} 字节，断开连接: {connectionId}");

                        await SettleAsync(WebSocketCloseStatus.MessageTooBig, "frame too large")
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
                        // 不认识就不认，不断连接：上行类型是开放集合，多出一条不认识的
                        // 不代表这条连接有问题
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

                    // 队列满就丢这一帧，让客户端知道该重试什么。以前这里是 await WriteAsync：
                    // 一条慢 Ack 把队列塞满之后，读循环就停在原地等，后面的心跳连读都读不出来，
                    // 客户端收不到 Pong 判定掉线重连——重连风暴反过来把存储压得更死。
                    // 丢的是回执不是消息：水位不推进，客户端稍后重报即可，不会丢消息
                    if (!serial.Writer.TryWrite(parsed))
                    {
                        RejectThrottled(parsed);
                    }
                }
            }
            finally
            {
                serial.Writer.TryComplete();

                // 正常路径已经在 SettleAsync 里排空过，这里是兜底（读循环被异常打断）。
                // 队列里可能还有没跑完的指令（客户端确认完就断线）：等它排空再收尾，
                // 否则那些确认会静默丢掉。派发卡死和以前一样会拖住请求，不额外加时限
                await serialLoop.ConfigureAwait(false);

                // 失效路径在对端没于 CloseHandshakeGrace 内回 Close 时会带着一次挂起的读退出
                // （见 CloseExpiredAsync），那次读写的正是这块租来的数组。这时直接归还，
                // 数组会被别的连接租走，两边同时往同一块内存里收帧，消息就串了。
                // 没读在途（绝大多数路径）就立刻还；有就等它结束再还，别让它跨连接复用
                if (pendingReceive is { IsCompleted: false } abandoned)
                {
                    _ = abandoned.ContinueWith(
                        static (completed, rented) =>
                        {
                            // 异常另有观察者（CloseExpiredAsync 收尾时挂的那条），这里只管归还
                            if (completed.IsFaulted) _ = completed.Exception;
                            ArrayPool<byte>.Shared.Return((byte[])rented!);
                        },
                        buffer,
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
                else
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            // 三条失效路径共用的收尾：先标记关闭，再排空串行队列，最后把关闭帧排进写队列。
            //
            // 顺序不能反：关闭帧一入队写循环就封队列，之后跑完的 Ack 回帧入不了队，
            // 以前还会反过来 Unbind→Abort —— 回帧丢了，关闭握手也可能被掐成 1006。
            // 先 MarkClosing 让这些回帧走「静默丢弃」，Ack 本身仍然落库。
            async Task SettleAsync(WebSocketCloseStatus status, string? description)
            {
                if (Interlocked.Exchange(ref settled, 1) != 0)
                {
                    return;
                }

                registry.MarkClosing(connectionId);

                serial.Writer.TryComplete();
                await serialLoop.ConfigureAwait(false);

                await registry.CloseOutputAsync(connectionId, status, description).ConfigureAwait(false);
            }

            // 收尾：先记指标再发关闭帧。三条失效路径共用，保证只发一次、顺序固定
            async Task CloseExpiredAsync()
            {
                KinnaMetrics.RecordWebSocketClosed(HubName, KinnaWebSocketCloseReasons.Expired);
                logger.Warning($"原生 WebSocket 连接已失效，主动断开: {connectionId}");

                await SettleAsync(WebSocketCloseStatus.PolicyViolation, "connection expired")
                    .ConfigureAwait(false);

                // 关闭帧写出后不能立刻摘连接：finally 里的 Unbind→Abort 会发 RST，把对端
                // 还没读走的关闭帧一起冲掉，客户端只看到 1006、拿不到 1008 这个原因。
                // 复用挂起的那次读（或补一次读）等对端回 Close 完成握手，有上限不等死。
                Task<ValueWebSocketReceiveResult>? reply = pendingReceive;
                if (reply is null)
                {
                    var handshakeBuffer = new byte[64];
                    reply = socket.ReceiveAsync(handshakeBuffer.AsMemory(), requestAborted).AsTask();
                }

                try
                {
                    await reply.WaitAsync(CloseHandshakeGrace, time).ConfigureAwait(false);
                }
                catch (TimeoutException) { }
                catch (OperationCanceledException) { }
                catch (WebSocketException) { }
                catch (ObjectDisposedException) { }
                finally
                {
                    // 超时或异常时这次读可能仍挂在 socket 上，它的异常得有人观测
                    _ = reply.ContinueWith(
                        static t => _ = t.Exception,
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
            }

            // 队列满时的回执：不查存储、只编码一帧，代价可以忽略。
            // 不能静默丢 —— 客户端收不到回帧，就以为服务端已经收下，
            // 那条 Ack 永远不会重报，水位就卡在这儿（表现是重连后一直在重复补拉）
            void RejectThrottled(KinnaClientMessage dropped)
            {
                // 标签值不能直接用上行 JSON 里的 type：那是客户端可控的任意字符串，
                // 每个新值都是一条新的时间序列。而这条指标恰恰只在「存储慢 + 有人在刷」时上涨，
                // 最需要它可信的时候被自己撑成一片噪音。白名单三档足够定位问题
                var method = dropped.Type switch
                {
                    KinnaInboundMethods.Ack => KinnaInboundMethods.Ack,
                    KinnaInboundMethods.Resync => KinnaInboundMethods.Resync,
                    _ => KinnaInboundDroppedMethods.Other
                };

                KinnaMetrics.RecordInboundDropped(HubName, method);

                switch (dropped.Type)
                {
                    case KinnaInboundMethods.Ack:
                        registry.Send(
                            connectionId,
                            KinnaClientMethods.Ack,
                            [new KinnaAckResult { Status = KinnaAckStatus.Throttled }]);
                        break;

                    case KinnaInboundMethods.Resync:
                        registry.Send(
                            connectionId,
                            KinnaClientMethods.Resync,
                            [
                                new KinnaResyncResult
                                {
                                    Throttled = true,

                                    // 被限流时一定为 false：这时候让客户端去补拉，
                                    // 只是把长连接的压力原样搬到数据库上
                                    NeedBackfill = false,
                                    ServerTime = time.GetUtcNow()
                                }
                            ]);
                        break;

                    default:

                        // 心跳走旁路，不会到这儿；其余未识别的指令本来就没有回帧语义
                        break;
                }
            }

            // 判定失效后只负责通知读循环，关闭由读循环统一做：
            // 两边都关的话会发两次关闭帧，第二帧还会把 socket 状态弄乱
            async Task NotifyExpiredAsync()
            {
                // 先叫醒读循环去发关闭帧，再取消在途指令；两边都不碰 socket 的读
                expiredSignal.TrySetResult(true);
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
                    while (await serial.Reader.WaitToReadAsync(dispatchToken).ConfigureAwait(false))
                    {
                        while (serial.Reader.TryRead(out var queued))
                        {
                            if (await DispatchOnceAsync(queued, connectionId, registry, scopes, logger, dispatchToken)
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
