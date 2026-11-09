using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using Viv.Contracts.Models;
using Viv.Fakes;
using Viv.Kinna.Connections;
using Viv.Kinna.Contracts;
using Viv.Kinna.Metrics;
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

        /// <summary>可改的状态：模拟对端先发关闭帧（CloseReceived）这类只在收尾窗口出现的状态</summary>
        public WebSocketState StateOverride { get; set; } = WebSocketState.Open;

        public override WebSocketState State => StateOverride;

        public override WebSocketCloseStatus? CloseStatus => null;

        public override string? CloseStatusDescription => null;

        public override string? SubProtocol => null;

        /// <summary>Abort 有没有被调到 —— 硬断的原生连接靠它断言</summary>
        public bool Aborted { get; private set; }

        /// <summary>写出去的关闭帧状态码，没发过就是 null</summary>
        public WebSocketCloseStatus? SentCloseStatus { get; private set; }

        /// <summary>收尾动作的先后顺序（close / abort），用来锁「先发帧再硬断」</summary>
        public IReadOnlyList<string> Calls
        {
            get { lock (_sent) return [.. _calls]; }
        }

        private readonly List<string> _calls = new();

        public override void Abort()
        {
            Aborted = true;

            lock (_sent)
            {
                _calls.Add("abort");
            }
        }

        public override void Dispose()
        {
        }

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription,
            CancellationToken cancellationToken)
        {
            SentCloseStatus = closeStatus;

            lock (_sent)
            {
                _calls.Add("close");
            }

            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription,
            CancellationToken cancellationToken)
        {
            SentCloseStatus = closeStatus;

            lock (_sent)
            {
                _calls.Add("close");
            }

            return Task.CompletedTask;
        }

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

    /// <summary>替身：写就抛，模拟对端 RST 把写循环打死。</summary>
    internal sealed class ThrowingWebSocket : RecordingWebSocket
    {
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType,
            bool endOfMessage, CancellationToken cancellationToken)
            => throw new WebSocketException("对端已重置");
    }

    /// <summary>
    /// 替身：第一帧卡住，放开后抛。模拟「关闭指令还在队列里、写循环死在前一条数据帧上」——
    /// 客户端刷新、踢人、停机都可能撞上这个时序
    /// </summary>
    internal sealed class FailingBlockingWebSocket : RecordingWebSocket
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _fail = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FirstSendStarted => _started.Task;

        public void Fail() => _fail.TrySetResult();

        public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType,
            bool endOfMessage, CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            await _fail.Task.ConfigureAwait(false);

            throw new WebSocketException("对端已重置");
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

        /// <summary>只给「指标不能记假 close_timeout」那条用例用的 hub 名：按它过滤，避免并行测试串台</summary>
        private const string CloseProbeHub = "KinnaCloseProbeHub";

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

        /// <summary>
        /// 监听本模块的一个 long 计数器。MeterListener 只收「开始监听之后」的测量，
        /// 所以每条用例都得在触发动作之前建好
        /// </summary>
        private static MeterListener ListenLong(
            string instrumentName,
            Action<List<KeyValuePair<string, object?>>> onMeasurement)
        {
            var listener = new MeterListener();

            listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == KinnaMetrics.MeterName && instrument.Name == instrumentName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };

            listener.SetMeasurementEventCallback<long>((_, _, tags, _) => onMeasurement([.. tags]));
            listener.Start();

            return listener;
        }

        private static string Tag(List<KeyValuePair<string, object?>> tags, string key)
        {
            foreach (var tag in tags)
            {
                if (tag.Key == key)
                {
                    return tag.Value?.ToString() ?? string.Empty;
                }
            }

            return string.Empty;
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
        public async Task CloseOutputAsync_关闭帧等不到写出时到点返回false而不是一直挂住()
        {
            // 写循环有可能先死（请求中止会取消它），队列里的关闭指令就再没人消费。
            // 没有超时兜底的话，调用方的 await 会挂到进程结束 —— 连着 socket 和请求作用域一起泄漏
            var time = new FakeTimeProvider();
            var registry = new KinnaWebSocketRegistry<TestHub>(8, time);
            var socket = new BlockingWebSocket();
            registry.Bind("ws:1", socket, CancellationToken.None);

            // 先把写循环卡在第一次 SendAsync 里，让后面的关闭指令排在它后面永远发不出去
            Assert.True(registry.Send("ws:1", "ReceiveMessage", [1]));
            await socket.FirstSendStarted;

            var closing = registry.CloseOutputAsync("ws:1", WebSocketCloseStatus.NormalClosure, "bye");

            // 时钟推过上限，返回 false 表示这一帧没发出去，调用方应当直接硬断
            time.Advance(TimeSpan.FromSeconds(6));

            Assert.False(await closing.WaitAsync(TimeSpan.FromSeconds(5)));
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
        public async Task 关闭帧之后的回帧只丢弃不硬断连接()
        {
            // 端点收到对端 Close 后会排空串行队列，里面跑完的 Ack 回帧正是在这个窗口到达。
            // 以前它会入队失败 → Unbind → Abort，把还没被客户端读走的关闭帧掐成 1006
            var registry = new KinnaWebSocketRegistry<TestHub>(8);
            var socket = new RecordingWebSocket();
            registry.Bind("ws:1", socket, CancellationToken.None);

            Assert.True(await registry.CloseOutputAsync("ws:1", WebSocketCloseStatus.NormalClosure, "bye"));

            Assert.False(registry.Send("ws:1", KinnaClientMethods.Ack, [1]));
            Assert.False(socket.Aborted);
            Assert.Equal(WebSocketCloseStatus.NormalClosure, socket.SentCloseStatus);
        }

        [Fact]
        public void 对端已发关闭帧时推送只丢弃不硬断连接()
        {
            // 对端先发 Close 之后 socket 状态是 CloseReceived，到 MarkClosing 之间还有一段窗口。
            // 这期间来的推送入队必然失败（IsOpen 要求 Open），若 IsClosing 只看「关闭帧已入队」，
            // Registry 会把它当慢客户端走 Unbind→Abort —— 我们那条关闭帧还没写出去，
            // 客户端拿到的是 1006，不是我们想给它的原因。上一版修过同一类问题，只漏了这几行
            var registry = new KinnaWebSocketRegistry<TestHub>(8);
            var socket = new RecordingWebSocket { StateOverride = WebSocketState.CloseReceived };
            registry.Bind("ws:1", socket, CancellationToken.None);

            Assert.False(registry.Send("ws:1", "ReceiveMessage", [1]));
            Assert.False(socket.Aborted);
            Assert.True(registry.IsLocal("ws:1"));
        }

        [Fact]
        public async Task 同一条连接只排一条关闭指令()
        {
            // 软踢、停机、端点收尾可能同时来排关闭帧。第二条会留在队列里永远没人处理
            // （写循环写完第一条就 return），等它的调用方白等满 CloseFrame，
            // 指标上再记一条假的 close_timeout
            var time = new FakeTimeProvider();
            var socket = new BlockingWebSocket();

            using var connection = new KinnaWebSocketConnection(
                "ws:1", nameof(TestHub), socket, 8, CancellationToken.None, time);

            connection.Start();
            Assert.True(connection.TryEnqueue("""{"method":"x"}"""u8.ToArray()));
            await socket.FirstSendStarted;

            var first = connection.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye");
            var second = connection.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye again");

            // 第二条不该排队：立即失败，调用方（Terminate / AbortAll）据此走硬断
            Assert.True(second.IsCompleted);
            Assert.False(await second);

            socket.Release();

            Assert.True(await first.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Single(socket.Calls, call => call == "close");
        }

        [Fact]
        public async Task 写循环在关闭指令之前死掉时立即返回false且不记close_timeout()
        {
            // 关闭指令排在卡住的数据帧后面，写循环在那帧上抛出去直接退出：指令永远轮不到。
            // 没人结算它，CloseOutputAsync 就要白等满 CloseFrame，再记一条假的 close_timeout ——
            // 而那根本不是「超时」，是写循环死了
            var time = new FakeTimeProvider();
            var socket = new FailingBlockingWebSocket();

            var reasons = new List<string>();
            using var listener = ListenLong("kinna.ws.closed", tags =>
            {
                // 指标是进程级共享的，用例并行跑时别的类记的 close_timeout 会串进来。
                // 用一个别的测试不会用的 hub 名，按标签过滤才是确定的
                if (Tag(tags, "hub") != CloseProbeHub)
                {
                    return;
                }

                lock (reasons)
                {
                    reasons.Add(Tag(tags, "reason"));
                }
            });

            using var connection = new KinnaWebSocketConnection(
                "ws:1", CloseProbeHub, socket, 8, CancellationToken.None, time);

            connection.Start();
            Assert.True(connection.TryEnqueue("""{"method":"x"}"""u8.ToArray()));
            await socket.FirstSendStarted;

            var closing = connection.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye");

            socket.Fail();

            // 不推时钟：正确实现立即返回 false，而不是走超时分支
            Assert.False(await closing.WaitAsync(TimeSpan.FromSeconds(5)));

            string[] seen;
            lock (reasons)
            {
                seen = [.. reasons];
            }

            // 真实的关闭原因要留痕，假的那条不能有
            Assert.Contains(KinnaWebSocketCloseReasons.SendFailed, seen);
            Assert.DoesNotContain(KinnaWebSocketCloseReasons.CloseTimeout, seen);
        }

        [Fact]
        public async Task 请求中止后关闭帧立即放弃而不是等满超时()
        {
            // 写循环的 _lifetime 连着 requestAborted：客户端刷新时它已经退出，
            // 队列里的关闭指令再没人消费。以前要白等满 WebSocketCloseTimeout 才返回
            var time = new FakeTimeProvider();
            var registry = new KinnaWebSocketRegistry<TestHub>(8, time);

            using var aborted = new CancellationTokenSource();
            aborted.Cancel();

            var socket = new RecordingWebSocket();
            registry.Bind("ws:1", socket, aborted.Token);

            // 不推时钟：正确实现应立即返回 false，而不是等超时分支
            var closing = registry.CloseOutputAsync(
                "ws:1", WebSocketCloseStatus.EndpointUnavailable, "connection aborted");

            Assert.True(closing.IsCompleted);
            Assert.False(await closing);
            Assert.Null(socket.SentCloseStatus);
        }

        [Fact]
        public async Task 写循环因发送异常退出后关闭帧也立即放弃()
        {
            // 对端 RST 会让 SendAsync 抛 WebSocketException，写循环直接退出：
            // 这时 _aborted 没置位、socket.State 往往还是 Open。只看这两样的存活判定
            // 会认为连接还能写，关闭帧入队后没人消费 —— 每条都要白等满 CloseFrame。
            // 请求中止那条路径上一版修过，异常退出这条是同源漏网
            var time = new FakeTimeProvider();
            var socket = new ThrowingWebSocket();

            using var connection = new KinnaWebSocketConnection(
                "ws:1", nameof(TestHub), socket, 8, CancellationToken.None, time);

            connection.Start();
            Assert.True(connection.TryEnqueue("""{"method":"x"}"""u8.ToArray()));

            // 写循环退出即这个任务完成。下面不推时钟：正确实现应立即返回，而不是等超时分支
            await connection.SenderCompletion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(connection.IsOpen);
            Assert.False(connection.TryEnqueue("""{"method":"y"}"""u8.ToArray()));

            var closing = connection.CloseOutputAsync(WebSocketCloseStatus.EndpointUnavailable, "gone");

            Assert.True(closing.IsCompleted);
            Assert.False(await closing);
            Assert.Null(socket.SentCloseStatus);
        }

        [Fact]
        public async Task 停机时队列满的连接不拖住AbortAll()
        {
            // 队列满时 EnqueueClose 塞不进去，写循环还在慢慢发、SenderCompletion 不会完成。
            // 把它算进等待，每次停机都要白等满宽限，而且它自己也收不到 1012
            var timeouts = new KinnaWebSocketTimeouts(
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(80));

            var registry = new KinnaWebSocketRegistry<TestHub>(1, timeouts);
            var socket = new BlockingWebSocket();
            registry.Bind("ws:1", socket, CancellationToken.None);

            Assert.True(registry.Send("ws:1", "A", [1]));
            await socket.FirstSendStarted;
            Assert.True(registry.Send("ws:1", "B", [2]));

            var stopwatch = Stopwatch.StartNew();
            registry.AbortAll();
            stopwatch.Stop();

            Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(60));
            Assert.Null(socket.SentCloseStatus);
            Assert.True(socket.Aborted);

            socket.Release();
        }

        [Fact]
        public void AbortAll_先发1012关闭帧再硬断()
        {
            // 直接 Abort 客户端只看到 1006，会当成断网立刻重连；滚动发布时整个机群一起重连，
            // 撞上的还是正在停的副本。1012 Service Restart 让客户端知道该退避
            var registry = new KinnaWebSocketRegistry<TestHub>(64);
            var socket = new RecordingWebSocket();
            registry.Bind("ws:1", socket, CancellationToken.None);

            registry.AbortAll();

            Assert.Equal(0, registry.Count);
            Assert.True(socket.Aborted);
            Assert.Equal((WebSocketCloseStatus)1012, socket.SentCloseStatus);

            // 顺序也要对：先 Abort 的话这一帧根本写不出去
            Assert.Equal(["close", "abort"], socket.Calls);
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
        public async Task 关闭SignalR时下行不再调用SignalR发送器()
        {
            var registry = new KinnaWebSocketRegistry<TestHub>(64);
            var socket = new RecordingWebSocket();
            registry.Bind("ws:1", socket, CancellationToken.None);
            registry.AddToGroup("ws:1", "g1");

            var signalr = new RecordingKinnaClientSender();
            var sender = new CompositeKinnaClientSender<TestHub>(registry, signalr, signalrEnabled: false);

            // 非本机目标：没有 SignalR 可兜底，只能丢弃，但不该抛
            await sender.SendToConnectionAsync("sig:1", "ReceiveMessage", [1]);
            await sender.SendToConnectionsAsync(["ws:1", "sig:1"], "ReceiveMessage", new { text = "hi" });
            await sender.SendToGroupAsync("g1", "ReceiveMessage", 1);
            await sender.AddToGroupAsync("sig:1", "g1");
            await sender.RemoveFromGroupAsync("sig:1", "g1");
            await sender.TerminateConnectionAsync("sig:1", KinnaClientMethods.ForceDisconnect, []);

            Assert.Empty(signalr.Calls);
            Assert.Empty(signalr.Added);
            Assert.Empty(signalr.Removed);

            // 本机原生连接不受影响：单发 1 帧 + 群发 1 帧
            await WaitUntilAsync(() => socket.Sent.Count == 2, "原生连接收到单发与群发");
        }

        [Fact]
        public async Task 关闭SignalR时踢人仍能断开本机原生连接()
        {
            var registry = new KinnaWebSocketRegistry<TestHub>(64);
            var socket = new RecordingWebSocket();
            registry.Bind("ws:1", socket, CancellationToken.None);

            var signalr = new RecordingKinnaClientSender();
            var sender = new CompositeKinnaClientSender<TestHub>(registry, signalr, signalrEnabled: false);

            await sender.TerminateConnectionAsync("ws:1", KinnaClientMethods.ForceDisconnect, []);

            await WaitUntilAsync(() => socket.Aborted, "原生连接被硬断");
            Assert.Empty(signalr.Calls);
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
        public async Task 软踢帧挤不进待发队列时记丢弃指标()
        {
            // 队列满就是慢客户端。硬断照旧，但这一帧没出去，客户端只看到一条普通断线、
            // 看不到「被谁踢的」。这条路径以前完全静默，指标是唯一能看见它的地方
            var dropped = new List<(string Hub, string Reason)>();
            using var listener = ListenLong(
                "kinna.send.orphaned",
                tags => dropped.Add((Tag(tags, "hub"), Tag(tags, "reason"))));

            var registry = new KinnaWebSocketRegistry<TestHub>(1);
            var socket = new BlockingWebSocket();
            registry.Bind("ws:1", socket, CancellationToken.None);

            // 第一帧被写循环取走、卡在 SendAsync 里；第二帧正好占满容量 1
            Assert.True(registry.Send("ws:1", KinnaClientMethods.ReceiveMessage, [new { n = 1 }]));
            await socket.FirstSendStarted.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(registry.Send("ws:1", KinnaClientMethods.ReceiveMessage, [new { n = 2 }]));

            registry.Terminate("ws:1", KinnaClientMethods.ForceDisconnect, [], TimeSpan.FromMilliseconds(100));

            Assert.Contains(("TestHub", KinnaSendDropReasons.WebSocketQueueFull), dropped);

            socket.Release();
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
