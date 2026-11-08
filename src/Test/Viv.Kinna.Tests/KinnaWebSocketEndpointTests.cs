using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Viv.Contracts.Models;
using Viv.Fakes;
using Viv.Kinna.Connections;
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

            await app.StopAsync();
        }

        private static string ConnectionIdOf(string frame)
        {
            using var document = JsonDocument.Parse(frame);
            return document.RootElement.GetProperty("args")[0].GetProperty("connectionId").GetString()!;
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

        private sealed class ThrowingPresence : IKinnaPresenceSink<TestHub>
        {
            public Task OnlineAsync(long subjectId, long userId)
                => throw new InvalidOperationException("presence down");

            public Task OfflineAsync(long subjectId, long userId) => Task.CompletedTask;

            public Task RenewAsync(long subjectId, long userId) => Task.CompletedTask;
        }
    }
}
