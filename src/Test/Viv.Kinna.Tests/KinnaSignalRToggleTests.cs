using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Viv.Contracts.Models;
using Viv.Fakes;
using Viv.Kinna.Connections;
using Viv.Kinna.Extensions;
using Viv.Kinna.Ports;
using Viv.Log;

namespace Viv.Kinna.Tests
{
    /// <summary>
    /// <see cref="Options.KinnaOptions.EnableSignalR"/> 的行为：关掉之后 SignalR 端点不该被映射，
    /// 但原生 WebSocket 那条链要照常工作。真起 Kestrel 验证，因为「端点有没有映射」只能从外面看。
    /// </summary>
    public class KinnaSignalRToggleTests
    {
        private const string HubPath = "/chat";
        private const string WebSocketPath = "/chat-ws";

        private sealed class FixedIdentityResolver : IKinnaIdentityResolver<TestHub>
        {
            public Task<VivContextContent?> ResolveAsync(HttpContext context)
                => Task.FromResult<VivContextContent?>(new VivContextContent
                {
                    AppId = 3,
                    SubjectId = 1,
                    UserId = 2
                });
        }

        private static async Task<WebApplication> StartAsync(bool enableSignalR)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");

            builder.Services.AddSingleton<ILoggerContract, RecordingLogger>();
            builder.Services.AddSingleton<IKinnaIdentityResolver<TestHub>, FixedIdentityResolver>();
            builder.Services.AddKinna<TestHub>(options =>
            {
                options.HubPath = HubPath;
                options.WebSocketPath = WebSocketPath;
                options.EnableSignalR = enableSignalR;
            });

            var app = builder.Build();
            app.MapKinnaHub<TestHub>();
            app.MapKinnaWebSocket<TestHub>();

            await app.StartAsync();
            return app;
        }

        private static HttpClient Client(WebApplication app) => new() { BaseAddress = new Uri(app.Urls.First()) };

        private static Task<HttpResponseMessage> NegotiateAsync(HttpClient client)
            => client.PostAsync(
                $"{HubPath}/negotiate?negotiateVersion=1",
                new StringContent(string.Empty));

        [Fact]
        public async Task 默认开启SignalR时hub端点存在()
        {
            await using var app = await StartAsync(enableSignalR: true);

            using var client = Client(app);
            using var response = await NegotiateAsync(client);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task 关闭SignalR时hub端点不映射()
        {
            await using var app = await StartAsync(enableSignalR: false);

            using var client = Client(app);
            using var response = await NegotiateAsync(client);

            // 即使宿主没删 MapKinnaHub 调用，也不该有端点
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task 关闭SignalR时原生WebSocket照常握手()
        {
            await using var app = await StartAsync(enableSignalR: false);

            using var client = new System.Net.WebSockets.ClientWebSocket();
            var uri = new Uri(app.Urls.First().Replace("http://", "ws://", StringComparison.Ordinal) + WebSocketPath);
            await client.ConnectAsync(uri, CancellationToken.None);

            var buffer = new byte[4096];
            var result = await client.ReceiveAsync(buffer, CancellationToken.None);
            var text = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);

            Assert.Equal(System.Net.WebSockets.WebSocketMessageType.Text, result.MessageType);
            Assert.Contains("\"method\":\"Connected\"", text);
        }

        [Fact]
        public async Task 两条传输都关掉时启动即告警()
        {
            // EnableSignalR=false 且 WebSocketPath 为空：这个 Hub 没有任何接入方式，
            // 推送全部落空，而宿主从表面上看不出来（服务照常启动、健康检查照常通过）。
            // 注册期拿不到最终配置（共享配置要到运行时才绑定），告警只能放在会话装配时
            var silent = await Scenario.WarnAsync(enableSignalR: false, webSocketPath: null);
            Assert.Contains(silent, x => x.Contains("没有任何传输可接入", StringComparison.Ordinal));

            // 只关 SignalR 但挂了原生端点不算空转，不该报
            var nativeOnly = await Scenario.WarnAsync(enableSignalR: false, webSocketPath: WebSocketPath);
            Assert.Empty(nativeOnly);

            var both = await Scenario.WarnAsync(enableSignalR: true, webSocketPath: null);
            Assert.Empty(both);
        }

        /// <summary>只装配容器，把「装配时打出来的警告」捞回来</summary>
        private static class Scenario
        {
            internal static async Task<List<string>> WarnAsync(bool enableSignalR, string? webSocketPath)
            {
                var logger = new RecordingLogger();
                var services = new ServiceCollection();
                services.AddLogging();   // 健康检查注册要 ILogger<T>
                services.AddSingleton<ILoggerContract>(logger);
                services.AddKinna<TestHub>(options =>
                {
                    options.EnableSignalR = enableSignalR;
                    options.WebSocketPath = webSocketPath ?? string.Empty;
                });

                await using var provider = services.BuildServiceProvider();

                // 配置在 KinnaHubSetup 这个单例被解析时才最终成型
                provider.GetRequiredService<IKinnaSession<TestHub>>();

                return logger.Warnings;
            }
        }
    }
}
