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

        /// <summary>软踢帧发出去之后，最多等这么久就让位给硬断。</summary>
        private static readonly TimeSpan DefaultTerminateGrace = TimeSpan.FromSeconds(1);

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

        public KinnaWebSocketRegistry(int queueCapacity)
        {
            _queueCapacity = queueCapacity > 0 ? queueCapacity : 512;
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
            var connection = new KinnaWebSocketConnection(connectionId, HubName, socket, _queueCapacity, requestAborted);

            if (!_connections.TryAdd(connectionId, connection))
            {
                connection.Dispose();
                throw new InvalidOperationException($"原生 WebSocket 连接 Id 重复: {connectionId}");
            }

            connection.Start();
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

            connection.TryEnqueue(Encode(method, args));

            // 软踢帧之后再排一个关闭指令：写循环排空后自己发关闭帧退出，
            // 客户端拿到的是一次正常关闭，而不是被 Abort 掐断的「异常断开」
            _ = connection.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "disconnected");

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

            _ = removed.FinishAsync(grace ?? DefaultTerminateGrace);
            return true;
        }

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

        /// <summary>停机用：断掉本进程所有原生连接</summary>
        public void AbortAll()
        {
            foreach (var connectionId in _connections.Keys)
            {
                KinnaMetrics.RecordWebSocketClosed(HubName, KinnaWebSocketCloseReasons.Shutdown);
                Unbind(connectionId);
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
    }
}
