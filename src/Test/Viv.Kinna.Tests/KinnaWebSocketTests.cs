using System.Net.WebSockets;
using System.Text;
using Viv.Contracts.Models;
using Viv.Fakes;
using Viv.Kinna.Connections;
using Viv.Kinna.Contracts;
using Viv.Kinna.Options;
using Viv.Kinna.Ports;
using Viv.Kinna.WebSockets;

namespace Viv.Kinna.Tests
{
    /// <summary>替身：记录写出去的每一帧文本。</summary>
    internal class RecordingWebSocket : WebSocket
    {
        private readonly List<string> _sent = new();

        public IReadOnlyList<string> Sent
        {
            get { lock (_sent) return [.. _sent]; }
        }

        public override WebSocketState State => WebSocketState.Open;

        public override WebSocketCloseStatus? CloseStatus => null;

        public override string? CloseStatusDescription => null;

        public override string? SubProtocol => null;

        /// <summary>Abort 有没有被调到 —— 硬断的原生连接靠它断言</summary>
        public bool Aborted { get; private set; }

        public override void Abort()
        {
            Aborted = true;
        }

        public override void Dispose()
        {
        }

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType,
            bool endOfMessage, CancellationToken cancellationToken)
        {
            lock (_sent)
            {
                _sent.Add(Encoding.UTF8.GetString(buffer.AsSpan()));
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>替身：第一帧卡住不返回，用来把队列撑满。</summary>
    internal sealed class BlockingWebSocket : RecordingWebSocket
    {
        private readonly TaskCompletionSource _firstSend = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FirstSendStarted => _firstSend.Task;

        public void Release() => _release.TrySetResult();

        public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType,
            bool endOfMessage, CancellationToken cancellationToken)
        {
            _firstSend.TrySetResult();
            await _release.Task.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 原生 WebSocket 的连接表、组表、发送分流和上行翻译。
    ///
    /// 端到端（真握手、真帧）要起 TestServer，这里不覆盖；发包路径和协议语义都在这一层，
    /// 端点壳只剩 DI 与生命周期。
    /// </summary>
    public class KinnaWebSocketTests
    {
        private const long SubjectId = 1;
        private const long UserId = 2;
        private const long AppId = 3;

        private static VivContextContent Identity()
            => new() { AppId = AppId, SubjectId = SubjectId, UserId = UserId };

        private static KinnaConnectionCoordinator<TestHub> CreateCoordinator(
            out RecordingKinnaClientSender sender,
            out KinnaConnectionPool pool)
        {
            pool = new KinnaConnectionPool();
            sender = new RecordingKinnaClientSender();

            var session = new StubKinnaSession<TestHub>(
                pool,
                sender,
                new KinnaKeys("kinna"),
                new KinnaOptions());

            return new KinnaConnectionCoordinator<TestHub>(
                session,
                new RecordingLogger(),
                null,
                new StubKinnaPresenceSink<TestHub>(),
                new StubKinnaDeliveryStore<TestHub>());
        }

        private static async Task WaitUntilAsync(Func<bool> condition, string what)
        {
            for (var i = 0; i < 200; i++)
            {
                if (condition())
                {
                    return;
                }

                await Task.Delay(10);
            }

            Assert.Fail($"等待超时: {what}");
        }

        [Fact]
        public async Task Send_把方法名和参数编成信封()
        {
            var registry = new KinnaWebSocketRegistry<TestHub>(64);
            var socket = new RecordingWebSocket();
            registry.Bind("ws:1", socket, CancellationToken.None);

            Assert.True(registry.Send("ws:1", "ReceiveMessage", [new { text = "你好" }]));

            await WaitUntilAsync(() => socket.Sent.Count == 1, "第一帧写出");

            // 方法名/参数的结构与 SignalR 的 target/arguments 对齐，中文直出不转义
            Assert.Equal("""{"method":"ReceiveMessage","args":[{"text":"你好"}]}""", socket.Sent[0]);
        }

        [Fact]
        public void Send_连接不在本进程时返回false()
        {
            var registry = new KinnaWebSocketRegistry<TestHub>(64);

            Assert.False(registry.Send("ws:unknown", "ReceiveMessage", []));
            Assert.Empty(registry.GetGroupMembers("g1"));
        }

        [Fact]
        public async Task SendMany_只序列化一次发给多条连接()
        {
            var registry = new KinnaWebSocketRegistry<TestHub>(64);
            var a = new RecordingWebSocket();
            var b = new RecordingWebSocket();
            registry.Bind("ws:a", a, CancellationToken.None);
            registry.Bind("ws:b", b, CancellationToken.None);

            var sent = registry.SendMany(["ws:a", "ws:b", "ws:missing"], "ReceiveMessage", [1]);

            Assert.Equal(2, sent);
            await WaitUntilAsync(() => a.Sent.Count == 1 && b.Sent.Count == 1, "两条连接各收一帧");
            Assert.Equal(a.Sent[0], b.Sent[0]);
        }

        [Fact]
        public async Task Send_队列满了断开连接而不是无限排队()
        {
            // 容量 1：第一帧被写循环取走后正在发，第二帧占满队列，第三帧就该把连接判死
            var registry = new KinnaWebSocketRegistry<TestHub>(1);
            var socket = new BlockingWebSocket();
            registry.Bind("ws:1", socket, CancellationToken.None);

            Assert.True(registry.Send("ws:1", "ReceiveMessage", [1]));
            await socket.FirstSendStarted;

            Assert.True(registry.Send("ws:1", "ReceiveMessage", [2]));
            Assert.False(registry.Send("ws:1", "ReceiveMessage", [3]));

            Assert.False(registry.IsLocal("ws:1"));
            socket.Release();
        }

        [Fact]
        public void Unbind_把连接从所有组里摘干净()
        {
            var registry = new KinnaWebSocketRegistry<TestHub>(64);
            var socket = new RecordingWebSocket();
            registry.Bind("ws:1", socket, CancellationToken.None);

            registry.AddToGroup("ws:1", "g1");
            registry.AddToGroup("ws:1", "g2");

            registry.Unbind("ws:1");

            Assert.False(registry.IsLocal("ws:1"));
            Assert.Empty(registry.GetGroupMembers("g1"));
            Assert.Empty(registry.GetGroupMembers("g2"));
            Assert.Equal(0, registry.Count);
        }

        [Fact]
        public async Task SendToGroup_只发给组内本机连接()
        {
            var registry = new KinnaWebSocketRegistry<TestHub>(64);
            var a = new RecordingWebSocket();
            var b = new RecordingWebSocket();
            registry.Bind("ws:a", a, CancellationToken.None);
            registry.Bind("ws:b", b, CancellationToken.None);

            registry.AddToGroup("ws:a", "g1");

            Assert.Equal(1, registry.SendToGroup("g1", "ReceiveMessage", [1]));

            await WaitUntilAsync(() => a.Sent.Count == 1, "组内连接收到");
            Assert.Empty(b.Sent);

            Assert.Equal(0, registry.SendToGroup("g-missing", "ReceiveMessage", [1]));
        }

        [Fact]
        public async Task SendToConnectionsAsync_按归属分流到两套传输()
        {
            var registry = new KinnaWebSocketRegistry<TestHub>(64);
            var socket = new RecordingWebSocket();
            registry.Bind("ws:1", socket, CancellationToken.None);

            var signalr = new RecordingKinnaClientSender();
            var sender = new CompositeKinnaClientSender<TestHub>(registry, signalr);

            await sender.SendToConnectionsAsync(["ws:1", "sig:1"], "ReceiveMessage", new { text = "hi" });

            await WaitUntilAsync(() => socket.Sent.Count == 1, "原生连接收到");

            var call = Assert.Single(signalr.Calls);
            Assert.Equal("Clients", call.TargetKind);
            Assert.Equal("sig:1", call.TargetId);
        }

        [Fact]
        public async Task AddToGroupAsync_原生连接进本地组表而不是SignalR()
        {
            var registry = new KinnaWebSocketRegistry<TestHub>(64);
            registry.Bind("ws:1", new RecordingWebSocket(), CancellationToken.None);

            var signalr = new RecordingKinnaClientSender();
            var sender = new CompositeKinnaClientSender<TestHub>(registry, signalr);

            await sender.AddToGroupAsync("ws:1", "g1");
            await sender.RemoveFromGroupAsync("ws:1", "g1");

            Assert.Empty(signalr.Added);
            Assert.Empty(signalr.Removed);
            Assert.Empty(registry.GetGroupMembers("g1"));
        }

        [Fact]
        public async Task SendToGroupAsync_两边都发()
        {
            var registry = new KinnaWebSocketRegistry<TestHub>(64);
            var socket = new RecordingWebSocket();
            registry.Bind("ws:1", socket, CancellationToken.None);
            registry.AddToGroup("ws:1", "g1");

            var signalr = new RecordingKinnaClientSender();
            var sender = new CompositeKinnaClientSender<TestHub>(registry, signalr);

            await sender.SendToGroupAsync("g1", "ReceiveMessage", 1);

            await WaitUntilAsync(() => socket.Sent.Count == 1, "原生连接收到组消息");
            Assert.Equal("Group", Assert.Single(signalr.Calls).TargetKind);
        }

        [Fact]
        public void Parse_只认json对象()
        {
            Assert.Equal("ping", KinnaWebSocketProtocol.Parse("""{"type":"ping"}"""u8.ToArray())!.Type);

            // 非 JSON / 空帧都当没收到，不能因为一条脏帧就断开连接
            Assert.Null(KinnaWebSocketProtocol.Parse("not json"u8.ToArray()));
            Assert.Null(KinnaWebSocketProtocol.Parse([]));
        }

        [Fact]
        public async Task Dispatch_ping回Pong并续租()
        {
            var coordinator = CreateCoordinator(out _, out var pool);
            await coordinator.RegisterAsync("ws:1", Identity());

            var outbound = await KinnaWebSocketProtocol.DispatchAsync(
                new KinnaClientMessage { Type = KinnaInboundMethods.Ping }, coordinator, "ws:1");

            Assert.Equal(KinnaClientMethods.Pong, outbound.Method);
            Assert.IsType<KinnaPong>(outbound.Payload);
            Assert.NotNull(pool.TryGet("ws:1"));
        }

        [Fact]
        public async Task Dispatch_ack带上消息Id()
        {
            var coordinator = CreateCoordinator(out _, out _);
            await coordinator.RegisterAsync("ws:1", Identity());

            var outbound = await KinnaWebSocketProtocol.DispatchAsync(
                new KinnaClientMessage { Type = KinnaInboundMethods.Ack, Ids = [7, 8] },
                coordinator,
                "ws:1");

            Assert.Equal(KinnaClientMethods.Ack, outbound.Method);

            // 载荷是结构化的结果，不再是裸水位：客户端要能区分「记下了」「被限流」「连接没了」
            var ack = Assert.IsType<KinnaAckResult>(outbound.Payload);
            Assert.Equal(KinnaAckStatus.Accepted, ack.Status);
            Assert.Equal(8, ack.Watermark);
        }

        [Fact]
        public async Task Dispatch_resync返回补齐建议()
        {
            var coordinator = CreateCoordinator(out _, out _);
            await coordinator.RegisterAsync("ws:1", Identity());

            var outbound = await KinnaWebSocketProtocol.DispatchAsync(
                new KinnaClientMessage { Type = KinnaInboundMethods.Resync, LastMessageId = 5 },
                coordinator,
                "ws:1");

            Assert.Equal(KinnaClientMethods.Resync, outbound.Method);
            Assert.IsType<KinnaResyncResult>(outbound.Payload);
        }

        [Fact]
        public async Task Dispatch_不认识的指令不回应答()
        {
            var coordinator = CreateCoordinator(out _, out _);
            await coordinator.RegisterAsync("ws:1", Identity());

            var outbound = await KinnaWebSocketProtocol.DispatchAsync(
                new KinnaClientMessage { Type = "unknown" }, coordinator, "ws:1");

            Assert.Null(outbound.Method);
        }

        [Fact]
        public async Task 默认准入只校验身份完整性()
        {
            var admission = new KinnaIdentityAdmission<TestHub>();

            Assert.False((await admission.CheckAsync(null)).Allowed);
            Assert.False((await admission.CheckAsync(new VivContextContent { AppId = 0, SubjectId = 1, UserId = 1 })).Allowed);
            Assert.False((await admission.CheckAsync(new VivContextContent { AppId = 1, SubjectId = 0, UserId = 1 })).Allowed);
            Assert.True((await admission.CheckAsync(Identity())).Allowed);
        }

        [Fact]
        public async Task Terminate_先发出软踢帧再硬断连接()
        {
            // 只入队不硬断的话，客户端不配合（冻结、忽略未知帧）就成了黑洞：
            // 连接表里没有它，推送永远到不了，它自己还以为连着
            var registry = new KinnaWebSocketRegistry<TestHub>(64);
            var socket = new RecordingWebSocket();
            registry.Bind("ws:1", socket, CancellationToken.None);

            Assert.True(registry.Terminate(
                "ws:1", KinnaClientMethods.ForceDisconnect, [], TimeSpan.FromMilliseconds(200)));

            await WaitUntilAsync(() => socket.Aborted, "连接被硬断");

            // 帧要真的写出去：立刻 Abort 会把刚入队的这帧丢掉，客户端就不知道为什么被踢
            var frame = Assert.Single(socket.Sent);
            Assert.Contains(KinnaClientMethods.ForceDisconnect, frame);
            Assert.False(registry.IsLocal("ws:1"));
            Assert.Empty(registry.GetGroupMembers("g1"));
        }

        [Fact]
        public void Terminate_连接不在本进程时返回false()
        {
            var registry = new KinnaWebSocketRegistry<TestHub>(64);

            // 调用方据此回落给 SignalR
            Assert.False(registry.Terminate("ws:unknown", KinnaClientMethods.ForceDisconnect, []));
        }

        [Fact]
        public async Task Terminate_从组里一并摘干净()
        {
            var registry = new KinnaWebSocketRegistry<TestHub>(64);
            var socket = new RecordingWebSocket();
            registry.Bind("ws:1", socket, CancellationToken.None);
            registry.AddToGroup("ws:1", "g1");
            registry.AddToGroup("ws:1", "g2");

            registry.Terminate("ws:1", KinnaClientMethods.ForceDisconnect, [], TimeSpan.Zero);
            await WaitUntilAsync(() => socket.Aborted, "连接被硬断");

            Assert.Empty(registry.GetGroupMembers("g1"));
            Assert.Empty(registry.GetGroupMembers("g2"));
        }

        [Fact]
        public async Task TerminateConnectionAsync_原生连接走硬断而不是普通发送()
        {
            var registry = new KinnaWebSocketRegistry<TestHub>(64);
            var socket = new RecordingWebSocket();
            registry.Bind("ws:1", socket, CancellationToken.None);

            var signalr = new RecordingKinnaClientSender();
            var sender = new CompositeKinnaClientSender<TestHub>(registry, signalr);

            await sender.TerminateConnectionAsync("ws:1", KinnaClientMethods.ForceDisconnect, []);

            await WaitUntilAsync(() => socket.Aborted, "原生连接被硬断");
            Assert.Empty(signalr.Calls);
        }

        [Fact]
        public async Task TerminateConnectionAsync_非原生连接回落给SignalR软踢()
        {
            var registry = new KinnaWebSocketRegistry<TestHub>(64);
            var signalr = new RecordingKinnaClientSender();
            var sender = new CompositeKinnaClientSender<TestHub>(registry, signalr);

            await sender.TerminateConnectionAsync("sig:1", KinnaClientMethods.ForceDisconnect, []);

            var call = Assert.Single(signalr.Calls);
            Assert.Equal("sig:1", call.TargetId);
            Assert.Equal(KinnaClientMethods.ForceDisconnect, call.Method);
        }

        [Fact]
        public async Task Dispatch_连接已失效时要求回完Pong就断开()
        {
            // 端点在 Ping 上发现连接表里没这条连接：Pong 照回（客户端才知道发生了什么），
            // 但要带着 Close 信号，由端点关掉 socket
            var coordinator = CreateCoordinator(out _, out var pool);
            await coordinator.RegisterAsync("ws:1", Identity());
            await coordinator.UnregisterAsync("ws:1");

            var outbound = await KinnaWebSocketProtocol.DispatchAsync(
                new KinnaClientMessage { Type = KinnaInboundMethods.Ping }, coordinator, "ws:1");

            Assert.Equal(KinnaClientMethods.Pong, outbound.Method);
            Assert.False(Assert.IsType<KinnaPong>(outbound.Payload).Alive);
            Assert.True(outbound.Close);
            Assert.Null(pool.TryGet("ws:1"));
        }

        [Fact]
        public async Task Dispatch_正常连接的Pong不要求断开()
        {
            var coordinator = CreateCoordinator(out _, out _);
            await coordinator.RegisterAsync("ws:1", Identity());

            var outbound = await KinnaWebSocketProtocol.DispatchAsync(
                new KinnaClientMessage { Type = KinnaInboundMethods.Ping }, coordinator, "ws:1");

            Assert.False(outbound.Close);
        }
    }
}
