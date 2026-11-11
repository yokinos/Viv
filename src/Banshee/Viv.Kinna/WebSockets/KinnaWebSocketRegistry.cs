using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Viv.Kinna.Contracts;
using Viv.Kinna.Metrics;

namespace Viv.Kinna.WebSockets
{
    /// <summary>
    /// 原生 WebSocket 的连接表与组表，按模块（THub）隔离。
    ///
    /// 这里和 <see cref="Connections.IKinnaConnectionPool"/> 是两件事：那个表存身份和租约，
    /// 这个表存传输句柄。一条连接只会落在其中一张里——WS 连接进这张，
    /// SignalR 连接进 SignalR 自己的表——所以 <see cref="IsLocal"/> 就能回答
    /// 「这条连接该走哪个发送器」。
    ///
    /// 组表只在本进程有效。跨实例投递沿用既有模型（每台副本收到全量事件、只推本机连接），
    /// 所以组只要在本机维护，不需要 Redis。
    /// </summary>
    public sealed class KinnaWebSocketRegistry<THub> where THub : Hub
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            // 中文直出，不转义成 \uXXXX。下行载荷全是对话文本，转义会让体积翻倍
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>
        /// 1012 Service Restart。RFC 6455 有这个码，但 <see cref="WebSocketCloseStatus"/> 没有对应成员，
        /// 只能强转（框架只拒绝 0-999 / 1006 / 1015，1012 是合法值）。
        /// 用它而不是 <c>Abort</c>：1012 告诉客户端「服务端在发布」，可以退避后重连；
        /// 1006 会被当成网络故障，客户端会立刻重连，撞上还在停的副本。
        /// </summary>
        private const WebSocketCloseStatus ServiceRestart = (WebSocketCloseStatus)1012;

        /// <summary>模块名，只用于指标标签</summary>
        private static readonly string HubName = typeof(THub).Name;

        private readonly ConcurrentDictionary<string, KinnaWebSocketConnection> _connections = new(StringComparer.Ordinal);

        // 组名 → 连接 Id
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _groups = new(StringComparer.Ordinal);

        // 连接 Id → 组名，断开时要按它把这人从所有组里摘干净，否则组表越用越大
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _memberships = new(StringComparer.Ordinal);

        // 只保护组表结构（建桶、摘桶）。成员写的是并发字典本身；
        // 这把锁只在连接建立/断开、加组退组时用到，不在推送路径上
        private readonly object _structureLock = new();

        private readonly int _queueCapacity;
        private readonly TimeProvider _time;
        private readonly KinnaWebSocketTimeouts _timeouts;

        public KinnaWebSocketRegistry(int queueCapacity, TimeProvider? timeProvider = null)
            : this(queueCapacity, KinnaWebSocketTimeouts.Default, timeProvider)
        {
        }

        public KinnaWebSocketRegistry(
            int queueCapacity,
            KinnaWebSocketTimeouts timeouts,
            TimeProvider? timeProvider = null)
        {
            _queueCapacity = queueCapacity > 0 ? queueCapacity : 512;
            _timeouts = Normalize(timeouts);
            _time = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// 本进程当前的原生连接数
        /// </summary>
        public int Count => _connections.Count;

        /// <summary>
        /// 本进程是否持有这条连接。发送器靠它决定走原生还是走 SignalR。
        /// </summary>
        public bool IsLocal(string connectionId) => _connections.ContainsKey(connectionId);

        /// <summary>
        /// 登记一条连接并启动它的发送循环
        /// </summary>
        internal void Bind(string connectionId, WebSocket socket, CancellationToken requestAborted)
        {
            var connection = new KinnaWebSocketConnection(
                connectionId, HubName, socket, _queueCapacity, requestAborted, _time, _timeouts);

            // 先拉起写循环再进表：IsOpen 会看写循环是否活着，如果连接先进了表，
            // 并发进来的推送会看到「写循环没起」把它当成死连接，走 Unbind 直接踢掉
            connection.Start();

            if (!_connections.TryAdd(connectionId, connection))
            {
                connection.Abort();
                connection.Dispose();
                throw new InvalidOperationException($"原生 WebSocket 连接 Id 重复: {connectionId}");
            }
        }

        /// <summary>
        /// 注销连接、停掉写循环，并把它从所有组里摘掉。
        /// 先摘表再清组：反过来的话，清组期间进来的推送还会往这条连接上写。
        /// </summary>
        internal void Unbind(string connectionId)
        {
            if (!_connections.TryRemove(connectionId, out var connection))
            {
                return;
            }

            connection.Abort();
            connection.Dispose();

            lock (_structureLock)
            {
                DetachGroups(connectionId);
            }
        }

        /// <summary>
        /// 软踢并断开一条原生连接：先入队一帧 <c>ForceDisconnect</c>，再让连接排空后硬断。
        /// 返回 false 表示这条连接不在本进程，调用方应当改走 SignalR。
        ///
        /// 只发帧不断开的话，客户端不配合（进程冻结、忽略未知帧）就成了黑洞：
        /// 连接表里的身份已经摘掉，推送永远到不了，而它自己还以为连着。
        /// </summary>
        internal bool Terminate(string connectionId, string method, object?[] args, TimeSpan? grace = null)
        {
            if (!_connections.TryGetValue(connectionId, out var connection))
            {
                return false;
            }

            // 软踢帧没排进去：客户端拿不到 ForceDisconnect，只看到一条普通断线。
            // 硬断照旧（下面还会排关闭指令），但这条路径以前完全静默 ——
            // 线上只能看到「踢了但客户端不知道自己为什么被踢」。
            // 已经在关闭流程里的连接不记：它本来就要断，这一帧丢不丢结果都一样
            if (!connection.TryEnqueue(Encode(method, args)) && connection.IsOpen)
            {
                KinnaMetrics.RecordOrphanedSend(HubName, KinnaSendDropReasons.WebSocketQueueFull);
            }

            // 软踢帧之后再排一个关闭指令：写循环排空后自己发关闭帧退出，
            // 客户端拿到的是一次正常关闭，而不是被 Abort 掐断的「异常断开」。
            // 只入队不等——等它写出去是 FinishAsync 宽限的事，这里再等一遍，
            // 因为宽限比 CloseTimeout 短，等的那次必然超时，指标上就多一条 close_timeout
            connection.EnqueueClose(WebSocketCloseStatus.NormalClosure, "disconnected");

            // 摘不到说明另一个线程已经收拾过这条连接（读循环退出、队列满），别再断一次
            if (!_connections.TryRemove(connectionId, out var removed))
            {
                return true;
            }

            lock (_structureLock)
            {
                DetachGroups(connectionId);
            }

            KinnaMetrics.RecordWebSocketClosed(HubName, KinnaWebSocketCloseReasons.Terminated);

            _ = removed.FinishAsync(grace ?? _timeouts.TerminateGrace);
            return true;
        }

        /// <summary>
        /// 标记连接进入关闭流程。端点在收到对端 Close / 判定失效 / 帧过大时调用：
        /// 之后串行队列里跑完的回帧只丢弃，不再走 Unbind→Abort，
        /// 否则会抢在客户端读到关闭帧之前把握手掐成 1006。
        /// </summary>
        internal void MarkClosing(string connectionId)
        {
            if (_connections.TryGetValue(connectionId, out var connection))
            {
                connection.MarkClosing();
            }
        }

        /// <summary>
        /// 这条连接是否已经进入关闭流程。端点用它避免在读写循环已经收过尾之后再关一次：
        /// 对端不读时关闭帧会等满 CloseFrame，两处都关就是白等两遍、close_timeout 记两条。
        /// </summary>
        internal bool IsClosing(string connectionId)
            => _connections.TryGetValue(connectionId, out var connection) && connection.IsClosing;

        /// <summary>
        /// 发一帧给单条连接。连接不在本进程或队列已满时返回 false。
        /// 队列满会直接断开这条连接——慢客户端继续留着只会吃内存，重连后走 Resync 对齐更便宜。
        /// </summary>
        internal bool Send(string connectionId, string method, object?[] args)
        {
            if (!_connections.TryGetValue(connectionId, out var connection))
            {
                return false;
            }

            return Enqueue(connectionId, connection, Encode(method, args));
        }

        /// <summary>
        /// 排空这条连接的待发帧之后发一个关闭帧，返回关闭帧是否发出。
        ///
        /// 关闭必须和普通下行帧走同一个写者：读循环直接对 socket 调 CloseOutput 会和
        /// 写循环并发，关闭帧可能抢在「为什么会关」的那一帧前面（失效的 Pong、ForceDisconnect），
        /// 客户端就只看得到断线，看不到原因。
        /// </summary>
        internal Task<bool> CloseOutputAsync(string connectionId, WebSocketCloseStatus status, string? description)
        {
            if (!_connections.TryGetValue(connectionId, out var connection))
            {
                // 连接已经不在表里（被踢、被清扫），没有可关的 socket
                return Task.FromResult(false);
            }

            return connection.CloseOutputAsync(status, description);
        }

        /// <summary>
        /// 发一帧给多条连接，返回成功入队的条数。序列化只做一次。
        /// </summary>
        internal int SendMany(IReadOnlyList<string> connectionIds, string method, object?[] args)
        {
            if (connectionIds.Count == 0)
            {
                return 0;
            }

            var frame = Encode(method, args);
            var sent = 0;

            foreach (var connectionId in connectionIds)
            {
                if (!_connections.TryGetValue(connectionId, out var connection))
                {
                    continue;
                }

                if (Enqueue(connectionId, connection, frame))
                {
                    sent++;
                }
            }

            return sent;
        }

        /// <summary>
        /// 发一帧给组内所有本机连接，返回成功入队的条数
        /// </summary>
        internal int SendToGroup(string groupName, string method, object?[] args)
        {
            if (!_groups.TryGetValue(groupName, out var members) || members.IsEmpty)
            {
                return 0;
            }

            return SendMany([.. members.Keys], method, args);
        }

        internal void AddToGroup(string connectionId, string groupName)
        {
            // 和 Unbind 里的摘桶同锁：否则会出现「刚 GetOrAdd 出来就被当成空桶摘掉」
            lock (_structureLock)
            {
                _groups.GetOrAdd(groupName, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal))
                    .TryAdd(connectionId, 0);

                _memberships.GetOrAdd(connectionId, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal))
                    .TryAdd(groupName, 0);
            }
        }

        internal void RemoveFromGroup(string connectionId, string groupName)
        {
            lock (_structureLock)
            {
                RemoveMember(connectionId, groupName);

                if (_memberships.TryGetValue(connectionId, out var groups)
                    && groups.TryRemove(groupName, out _)
                    && groups.IsEmpty)
                {
                    _memberships.TryRemove(connectionId, out _);
                }
            }
        }

        /// <summary>测试和排障用</summary>
        internal IReadOnlyList<string> GetGroupMembers(string groupName)
            => _groups.TryGetValue(groupName, out var members) ? [.. members.Keys] : [];

        /// <summary>
        /// 停机用：给本进程每条原生连接补一个 1012 关闭帧，摘表，然后**放手**。
        ///
        /// 直接 Abort 的话客户端只看到 1006，会当成网络故障立刻重连——滚动发布时整机群的
        /// 客户端同时重连，撞上的还是正在停的副本。1012 明确告诉它是「服务重启」。
        ///
        /// 帧写出去之后不能立刻 Abort：Abort 发的是 RST，会把对端还没读走的那一帧冲掉，
        /// 客户端看到的还是 1006（实测错因就是 WSAECONNRESET）。所以写成功的连接交给
        /// <see cref="KinnaWebSocketConnection.Abandon"/> 让请求线程正常收尾，只有「帧压根
        /// 没写出去」的连接才硬断。
        ///
        /// 等待有上限（<see cref="KinnaWebSocketTimeouts.ShutdownGrace"/>）：这是同步回调，不能久等。
        /// </summary>
        public void AbortAll()
        {
            var closing = new List<KinnaWebSocketConnection>();

            foreach (var connectionId in _connections.Keys)
            {
                if (!_connections.TryGetValue(connectionId, out var connection))
                {
                    continue;
                }

                // 队列满时这一帧根本塞不进去：写循环还在慢慢发，关闭指令不会完成，
                // 算进等待只会让每次停机都白等满宽限，而且这条连接自己也收不到 1012。
                // 跳过等待，交给下面的硬断
                if (!connection.EnqueueClose(ServiceRestart, "server restart"))
                {
                    KinnaMetrics.RecordWebSocketClosed(HubName, KinnaWebSocketCloseReasons.ShutdownUnsent);
                    continue;
                }

                KinnaMetrics.RecordWebSocketClosed(HubName, KinnaWebSocketCloseReasons.Shutdown);
                closing.Add(connection);
            }

            try
            {
                // 关闭帧写出 = 帧已经在线上。阻塞这几百毫秒是刻意的：ApplicationStopping 是
                // 同步回调，不在这儿等，后面的 Kestrel 收尾会直接把 socket 掐掉，帧就丢了。
                //
                // 等 CloseOutcome 而不是 SenderCompletion：后者在「写循环异常退出」时也会完成，
                // 那种情况帧并没有出去，需要走下面的硬断
                Task.WhenAll(closing.ConvertAll(static connection => connection.CloseOutcome))
                    .Wait(_timeouts.ShutdownGrace);
            }
            catch (AggregateException)
            {
                // 写循环的异常不外抛（见 RunSenderAsync），这里只是兜一层，别让停机流程跟着炸
            }

            // 摘表要在帧写完之后：Abandon / Abort 都会让这条连接再也写不出去
            foreach (var connectionId in _connections.Keys)
            {
                if (!_connections.TryRemove(connectionId, out var connection))
                {
                    continue;
                }

                lock (_structureLock)
                {
                    DetachGroups(connectionId);
                }

                if (connection.CloseWritten)
                {
                    // 帧在线上：留着 socket 让对端把帧读走再握手结束，硬断会把帧冲掉。
                    // 宽限用 TerminateGrace（「给了关闭指令之后最多再等多久才硬断」），
                    // 对端不回 Close 时到点收走，别让一条不配合的连接拖住整次停机
                    connection.Abandon(_timeouts.TerminateGrace);
                    continue;
                }

                connection.Abort();
                connection.Dispose();
            }
        }

        /// <summary>把一条连接从所有组里摘掉。调用方必须持有 <see cref="_structureLock"/>。</summary>
        private void DetachGroups(string connectionId)
        {
            if (!_memberships.TryRemove(connectionId, out var groups))
            {
                return;
            }

            foreach (var groupName in groups.Keys)
            {
                RemoveMember(connectionId, groupName);
            }
        }

        /// <summary>摘掉一个成员，桶空了顺手把桶也摘掉。调用方必须持有 <see cref="_structureLock"/>。</summary>
        private void RemoveMember(string connectionId, string groupName)
        {
            if (_groups.TryGetValue(groupName, out var members)
                && members.TryRemove(connectionId, out _)
                && members.IsEmpty)
            {
                // 空组桶留着就是泄漏：会话越开越多，每个组名都会永久占一本空字典
                _groups.TryRemove(groupName, out _);
            }
        }

        private bool Enqueue(string connectionId, KinnaWebSocketConnection connection, byte[] frame)
        {
            if (connection.TryEnqueue(frame))
            {
                return true;
            }

            // 关闭流程已经开始：这一帧注定发不出去（收到过对端 Close，或服务端已排了关闭帧）。
            // 再走 Unbind→Abort 只会抢掉还没被客户端读走的关闭帧，客户端看到的是 1006 而不是原因
            if (connection.IsClosing)
            {
                return false;
            }

            // 队列满 = 慢客户端被踢。这条路径以前完全静默：连接没了、消息也没了，
            // 线上只能看到「客户端莫名其妙断线」，归因不了。
            // IsOpen 为 false 说明是并发收尾，不算慢客户端，别冤枉它
            if (connection.IsOpen)
            {
                KinnaMetrics.RecordWebSocketClosed(HubName, KinnaWebSocketCloseReasons.QueueFull);
            }

            Unbind(connectionId);
            return false;
        }

        private static byte[] Encode(string method, object?[] args)
            => JsonSerializer.SerializeToUtf8Bytes(new KinnaServerEnvelope(method, args), JsonOptions);

        /// <summary>
        /// 非正的时限没有意义（<c>WaitAsync</c> 会直接抛），一律回落到默认值。
        /// 配置里写 0 想表达「不等」的场景不存在——不等就不该发这一帧。
        ///
        /// 「宽限必须短于 CloseFrame」这条不变式在这里硬校验，而不是只写在注释里：
        /// 配反了不会报错，只会在每次关闭时多记一条假的 <c>close_timeout</c>，
        /// 指标一旦失真，后面就没人看它了。
        /// </summary>
        private static KinnaWebSocketTimeouts Normalize(KinnaWebSocketTimeouts? timeouts)
        {
            var fallback = KinnaWebSocketTimeouts.Default;

            if (timeouts is null)
            {
                return fallback;
            }

            var closeFrame = Positive(timeouts.CloseFrame, fallback.CloseFrame);
            var terminateGrace = Positive(timeouts.TerminateGrace, fallback.TerminateGrace);
            var shutdownGrace = Positive(timeouts.ShutdownGrace, fallback.ShutdownGrace);

            if (terminateGrace >= closeFrame || shutdownGrace >= closeFrame)
            {
                throw new InvalidOperationException(
                    $"关闭时限配反了：CloseFrame={closeFrame}, TerminateGrace={terminateGrace}, "
                    + $"ShutdownGrace={shutdownGrace}。后两个必须短于 CloseFrame，"
                    + "否则先到的宽限会把还在等的关闭帧判成失败（close_timeout 虚高）");
            }

            return new KinnaWebSocketTimeouts(closeFrame, terminateGrace, shutdownGrace);
        }

        private static TimeSpan Positive(TimeSpan value, TimeSpan fallback)
            => value > TimeSpan.Zero ? value : fallback;
    }
}
