using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Viv.Contracts.Models;
using Viv.Fakes;
using Viv.Kinna.Connections;
using Viv.Kinna.Contracts;
using Viv.Kinna.Extensions;
using Viv.Kinna.Ports;
using Viv.Kinna.WebSockets;
using Viv.Log;

namespace Viv.Kinna.Tests
{
    /// <summary>
    /// 原生 WebSocket 端点的端到端：真起 Kestrel、真握手、真收发。
    ///
    /// 这一层只验证「壳」——握手回执能出去、上行帧能被翻译、关闭能触发注销。
    /// 协议语义本身由 <see cref="KinnaWebSocketTests"/> 覆盖。
    /// </summary>
    public class KinnaWebSocketEndpointTests
    {
        private const long SubjectId = 1;
        private const long UserId = 2;
        private const long AppId = 3;

        private sealed class FixedIdentityResolver : IKinnaIdentityResolver<TestHub>
        {
            public Task<VivContextContent?> ResolveAsync(HttpContext context)
                => Task.FromResult<VivContextContent?>(new VivContextContent
                {
                    AppId = AppId,
                    SubjectId = SubjectId,
                    UserId = UserId
                });
        }

        private static async Task<WebApplication> StartAsync(Action<IServiceCollection>? configureServices = null)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");

            builder.Services.AddSingleton<ILoggerContract, RecordingLogger>();
            builder.Services.AddSingleton<IKinnaIdentityResolver<TestHub>, FixedIdentityResolver>();
            builder.Services.AddKinna<TestHub>(options => options.WebSocketPath = "/ws/chat");
            configureServices?.Invoke(builder.Services);

            var app = builder.Build();
            app.MapKinnaWebSocket<TestHub>();

            await app.StartAsync();
            return app;
        }

        private static Uri WebSocketUri(WebApplication app)
            => new(app.Urls.First().Replace("http://", "ws://", StringComparison.Ordinal) + "/ws/chat");

        private static async Task<string> ReceiveAsync(ClientWebSocket client)
        {
            var buffer = new byte[8 * 1024];
            var result = await client.ReceiveAsync(buffer, CancellationToken.None);

            Assert.Equal(WebSocketMessageType.Text, result.MessageType);
            return Encoding.UTF8.GetString(buffer, 0, result.Count);
        }

        [Fact]
        public async Task 握手后收到Connected帧_ping收到Pong帧()
        {
            await using var app = await StartAsync();

            using var client = new ClientWebSocket();
            await client.ConnectAsync(WebSocketUri(app), CancellationToken.None);

            var connected = await ReceiveAsync(client);
            Assert.Contains("\"method\":\"Connected\"", connected);
            Assert.Contains("\"connectionId\":\"ws:", connected);

            await client.SendAsync("""{"type":"ping"}"""u8.ToArray(), WebSocketMessageType.Text, true,
                CancellationToken.None);

            var pong = await ReceiveAsync(client);
            Assert.Contains("\"method\":\"Pong\"", pong);
            Assert.Contains("\"leaseExpiresAt\"", pong);

            await client.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            await app.StopAsync();
        }

        [Fact]
        public async Task 不认识的指令不回帧也不断连接()
        {
            await using var app = await StartAsync();

            using var client = new ClientWebSocket();
            await client.ConnectAsync(WebSocketUri(app), CancellationToken.None);

            await ReceiveAsync(client);

            await client.SendAsync("""{"type":"whatever"}"""u8.ToArray(), WebSocketMessageType.Text, true,
                CancellationToken.None);

            // 紧接着发 ping：如果服务端因为那条脏指令断了连接，这里会拿不到 Pong，而是直接关闭
            await client.SendAsync("""{"type":"ping"}"""u8.ToArray(), WebSocketMessageType.Text, true,
                CancellationToken.None);

            var pong = await ReceiveAsync(client);
            Assert.Contains("\"method\":\"Pong\"", pong);

            await client.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            await app.StopAsync();
        }

        [Fact]
        public async Task 未配置路径时不挂端点()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton<ILoggerContract, RecordingLogger>();
            builder.Services.AddSingleton<IKinnaIdentityResolver<TestHub>, FixedIdentityResolver>();
            builder.Services.AddKinna<TestHub>();

            await using var app = builder.Build();
            app.MapKinnaWebSocket<TestHub>();
            await app.StartAsync();

            using var client = new ClientWebSocket();

            // 没有 WebSocketPath 就什么都不挂，请求落到 404 而不是被当成 WS 接受
            await Assert.ThrowsAsync<WebSocketException>(
                () => client.ConnectAsync(WebSocketUri(app), CancellationToken.None));

            await app.StopAsync();
        }

        [Fact]
        public async Task 群成员查询失败不影响握手()
        {
            // 群服务抖一下不该让整条连接建不起来：单聊和按人推送都不依赖群成员
            await using var app = await StartAsync(services =>
                services.AddSingleton<IKinnaGroupSource<TestHub>, ThrowingGroupSource>());

            using var client = new ClientWebSocket();
            await client.ConnectAsync(WebSocketUri(app), CancellationToken.None);

            var connected = await ReceiveAsync(client);
            Assert.Contains("\"method\":\"Connected\"", connected);

            // 端口真的被调到了（不是「没注册所以跳过」，那样这条用例就白测了）
            Assert.True(ThrowingGroupSource.Calls > 0);

            var session = app.Services.GetRequiredService<IKinnaSession<TestHub>>();
            Assert.Single(session.Pool.GetConnectionIds(SubjectId, UserId));

            await client.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            await app.StopAsync();
        }

        [Fact]
        public async Task 注册抛异常时连接从传输表和连接表里都摘干净()
        {
            // RegisterAsync 抛在 Bind 之后、finally 之前时，以前会留下一条 socket 已死、
            // registry 里还在的僵尸连接，随连接尝试次数线性泄漏
            await using var app = await StartAsync(services =>
                services.AddSingleton<IKinnaPresenceSink<TestHub>, ThrowingPresence>());

            var registry = app.Services.GetRequiredService<KinnaWebSocketRegistry<TestHub>>();
            var session = app.Services.GetRequiredService<IKinnaSession<TestHub>>();

            using var client = new ClientWebSocket();
            await client.ConnectAsync(WebSocketUri(app), CancellationToken.None);

            // 服务端注册失败会主动关连接：客户端这边读到关闭帧或异常都算数
            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                var buffer = new byte[256];
                while (true)
                {
                    var result = await client.ReceiveAsync(buffer, CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                        throw new InvalidOperationException("服务端关闭了连接");
                }
            });

            // 关闭帧发出去时 Unbind 已经做完，注销紧跟其后
            await WaitUntilAsync(() => registry.Count == 0 && session.Pool.GetConnectionIds(SubjectId, UserId).Count == 0,
                "连接从两张表里摘干净");

            Assert.Equal(0, registry.Count);
            Assert.Empty(session.Pool.GetConnections(SubjectId));
        }

        [Fact]
        public async Task 连接已失效时先收到Pong再收到关闭帧()
        {
            // 这条用例盯的是「关闭帧必须排在应答帧后面」。关闭以前是读循环直接对 socket 写的，
            // 会和写循环并发：关闭帧抢到前面就把 Pong 吃掉，客户端只看到断线，看不到原因
            await using var app = await StartAsync();

            using var client = new ClientWebSocket();
            await client.ConnectAsync(WebSocketUri(app), CancellationToken.None);

            var connected = await ReceiveAsync(client);
            var connectionId = ConnectionIdOf(connected);

            // 模拟超租约被清扫、或被别的副本踢掉：身份从连接表里摘掉，socket 还在客户端手里
            var session = app.Services.GetRequiredService<IKinnaSession<TestHub>>();
            Assert.NotNull(session.Pool.Remove(connectionId));

            await client.SendAsync("""{"type":"ping"}"""u8.ToArray(), WebSocketMessageType.Text, true,
                CancellationToken.None);

            // 先告诉客户端「为什么」：这条连接已经不算数了
            var pong = await ReceiveAsync(client);
            Assert.Contains("\"method\":\"Pong\"", pong);
            Assert.Contains("\"alive\":false", pong);

            // 再断。顺序反过来的话，上面那条断言会超时或直接读到关闭帧
            var buffer = new byte[64];
            var close = await client.ReceiveAsync(buffer, CancellationToken.None);
            Assert.Equal(WebSocketMessageType.Close, close.MessageType);

            // 原因要能看出来：失效走 PolicyViolation，不是 finally 兜底的 EndpointUnavailable
            Assert.Equal(WebSocketCloseStatus.PolicyViolation, client.CloseStatus);

            await app.StopAsync();
        }

        [Fact]
        public async Task 握手帧先于群推送()
        {
            // 加组一完成，群推送就可能到达这条连接，握手帧必须赶在它前面。
            // 以前 Connected 要等 RegisterAsync 返回才发，查群慢时客户端第一帧就是业务帧
            var groups = new GatedGroupSource();
            await using var app = await StartAsync(services =>
                services.AddSingleton<IKinnaGroupSource<TestHub>>(groups));

            using var client = new ClientWebSocket();
            await client.ConnectAsync(WebSocketUri(app), CancellationToken.None);

            // 注册卡在查群：连接已登记、传输层也已 Bind，正好是握手帧该已经发出去的时刻
            await groups.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var session = app.Services.GetRequiredService<IKinnaSession<TestHub>>();
            var connectionId = Assert.Single(session.Pool.GetConnectionIds(SubjectId, UserId));

            await session.Sender.SendToConnectionAsync(
                connectionId, KinnaClientMethods.ReceiveMessage, [new { text = "early" }]);

            groups.Release.TrySetResult();

            var first = await ReceiveAsync(client);
            Assert.Contains("\"method\":\"Connected\"", first);

            var second = await ReceiveAsync(client);
            Assert.Contains("\"method\":\"ReceiveMessage\"", second);

            await client.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            await app.StopAsync();
        }

        private static string ConnectionIdOf(string frame)
        {
            using var document = JsonDocument.Parse(frame);
            return document.RootElement.GetProperty("args")[0].GetProperty("connectionId").GetString()!;
        }

        [Fact]
        public async Task 慢Ack不拖住Pong()
        {
            // 读循环原本是严格串行的：Ack 卡在存储上时心跳排在它后面，客户端收不到 Pong
            // 就会以为连接死了然后重连——重连风暴反过来把存储压得更死，越忙越断。
            // 所以心跳走旁路：不排队，但关闭仍然由读循环发起
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var store = new StubKinnaDeliveryStore<TestHub> { AckGate = gate.Task };

            await using var app = await StartAsync(
                services => services.AddSingleton<IKinnaDeliveryStore<TestHub>>(store));

            using var client = new ClientWebSocket();
            await client.ConnectAsync(WebSocketUri(app), CancellationToken.None);
            await ReceiveAsync(client);

            // 这条 Ack 会一直卡在存储里
            await client.SendAsync("""{"type":"ack","ids":[1]}"""u8.ToArray(), WebSocketMessageType.Text, true,
                CancellationToken.None);

            // 紧跟其后的心跳不该排队等它
            await client.SendAsync("""{"type":"ping"}"""u8.ToArray(), WebSocketMessageType.Text, true,
                CancellationToken.None);

            var pong = await ReceiveAsync(client).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Contains("\"method\":\"Pong\"", pong);

            // Ack 放行之后应答照旧回来：旁路只挪走了心跳，串行语义没动
            gate.SetResult();

            var ack = await ReceiveAsync(client).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Contains("\"method\":\"Ack\"", ack);

            await client.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            await app.StopAsync();
        }

        [Fact]
        public async Task 声明子协议时按kinna_v1协商()
        {
            await using var app = await StartAsync();

            using var client = new ClientWebSocket();
            client.Options.AddSubProtocol(KinnaWebSocketProtocols.V1);
            await client.ConnectAsync(WebSocketUri(app), CancellationToken.None);

            // 101 之前就谈定了版本，以后改上行帧格式才有分派入口
            Assert.Equal(KinnaWebSocketProtocols.V1, client.SubProtocol);
            Assert.Contains("\"method\":\"Connected\"", await ReceiveAsync(client));

            await client.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            await app.StopAsync();
        }

        [Fact]
        public async Task 只声明不认识的子协议时拒绝连接()
        {
            await using var app = await StartAsync();

            using var client = new ClientWebSocket();
            client.Options.AddSubProtocol("kinna.v9");

            // 认不了就别让它连上：101 之后再说「不支持」只能靠关帧，
            // 而客户端在此之前已经按错的格式发过帧了
            await Assert.ThrowsAsync<WebSocketException>(
                () => client.ConnectAsync(WebSocketUri(app), CancellationToken.None));

            await app.StopAsync();
        }

        [Fact]
        public async Task 终结点可以挂宿主策略()
        {
            // 限流、授权、OpenAPI 标签都读 endpoint 元数据；没有这个入口，这层壳就是策略黑区，
            // 加了 [EnableRateLimiting] 的人会以为生效了其实没有
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton<ILoggerContract, RecordingLogger>();
            builder.Services.AddSingleton<IKinnaIdentityResolver<TestHub>, FixedIdentityResolver>();
            builder.Services.AddKinna<TestHub>(options => options.WebSocketPath = "/ws/chat");

            await using var app = builder.Build();
            app.MapKinnaWebSocket<TestHub>(configure: endpoint => endpoint.WithMetadata(new PolicyMarker()));

            // 终结点要等路由初始化之后才在 EndpointDataSource 里
            await app.StartAsync();

            var route = app.Services.GetRequiredService<EndpointDataSource>()
                .Endpoints
                .OfType<RouteEndpoint>()
                .Single(e => e.RoutePattern.RawText == "/ws/chat");

            Assert.Contains(route.Metadata, metadata => metadata is PolicyMarker);

            await app.StopAsync();
        }

        /// <summary>替身：代表宿主想挂的任何策略（限流、授权、标签…）</summary>
        private sealed class PolicyMarker
        {
        }

        [Fact]
        public async Task 多Hub配了不同的KeepAlive时启动报错()
        {
            // UseWebSockets 是进程级的，第二个 Hub 的值根本不会生效。静默取第一个的话，
            // 「我明明配了 60 秒」这类问题要到线上抓包才发现
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton<ILoggerContract, RecordingLogger>();
            builder.Services.AddSingleton<IKinnaIdentityResolver<TestHub>, FixedIdentityResolver>();
            builder.Services.AddSingleton<IKinnaIdentityResolver<SecondTestHub>, SecondIdentityResolver>();
            builder.Services.AddKinna<TestHub>(options =>
            {
                options.WebSocketPath = "/ws/chat";
                options.WebSocketKeepAliveSeconds = 30;
            });
            builder.Services.AddKinna<SecondTestHub>(options =>
            {
                options.WebSocketPath = "/ws/other";
                options.WebSocketKeepAliveSeconds = 60;
            });

            var app = builder.Build();
            app.MapKinnaWebSocket<TestHub>();

            Assert.Throws<InvalidOperationException>(() => app.MapKinnaWebSocket<SecondTestHub>());

            await app.DisposeAsync();
        }

        public sealed class SecondTestHub : Hub
        {
        }

        private sealed class SecondIdentityResolver : IKinnaIdentityResolver<SecondTestHub>
        {
            public Task<VivContextContent?> ResolveAsync(HttpContext context)
                => Task.FromResult<VivContextContent?>(new VivContextContent
                {
                    AppId = AppId,
                    SubjectId = SubjectId,
                    UserId = UserId
                });
        }

        [Fact]
        public async Task 停机时主动断开本机原生连接()
        {
            await using var app = await StartAsync();

            using var client = new ClientWebSocket();
            await client.ConnectAsync(WebSocketUri(app), CancellationToken.None);
            await ReceiveAsync(client);

            var registry = app.Services.GetRequiredService<KinnaWebSocketRegistry<TestHub>>();
            Assert.Equal(1, registry.Count);

            await app.StopAsync();

            // ApplicationStopping 里接了 AbortAll：连接表要当场清空。
            // 不接的话 socket 只交给 Kestrel 的连接关闭超时去收，滚动发布期间客户端要空等几秒
            Assert.Equal(0, registry.Count);

            // 客户端看到的是 1012（Service Restart），不是硬断的 1006：前者可以退避重连，
            // 后者会被当成网络故障——滚动发布时整机群同时重连，撞上的还是正在停的副本
            var buffer = new byte[256];
            var received = await client
                .ReceiveAsync(buffer, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(WebSocketMessageType.Close, received.MessageType);
            Assert.Equal((WebSocketCloseStatus)1012, client.CloseStatus);
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

        private sealed class ThrowingGroupSource : IKinnaGroupSource<TestHub>
        {
            private static int _calls;

            internal static int Calls => Volatile.Read(ref _calls);

            public Task<List<long>> GetUserGroupIdsAsync(long subjectId, long userId, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref _calls);
                throw new InvalidOperationException("group service down");
            }
        }

        /// <summary>查群卡住，用来观察「注册还没完成时推送能不能到」。</summary>
        private sealed class GatedGroupSource : IKinnaGroupSource<TestHub>
        {
            public TaskCompletionSource Entered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource Release { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public async Task<List<long>> GetUserGroupIdsAsync(
                long subjectId, long userId, CancellationToken cancellationToken = default)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
                return [100];
            }
        }

        private sealed class ThrowingPresence : IKinnaPresenceSink<TestHub>
        {
            public Task OnlineAsync(long subjectId, long userId)
                => throw new InvalidOperationException("presence down");

            public Task OfflineAsync(long subjectId, long userId) => Task.CompletedTask;

            public Task RenewAsync(long subjectId, long userId) => Task.CompletedTask;
        }
    }
}
