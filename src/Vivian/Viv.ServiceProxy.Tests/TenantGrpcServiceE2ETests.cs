using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Viv.Contracts.Models;
using Viv.Echo.Grpc;
using Viv.Sandrone.Impl;
using Viv.ServiceProxy.Protos;
using Viv.ServiceProxy.Tests.Grpc;

namespace Viv.ServiceProxy.Tests
{
    /// <summary>
    /// 端到端：客户端拦截器注入 x-viv-* 头 → 服务端拦截器恢复租户上下文 →
    /// 服务实现读到 AppId/SubjectId/UserId。四种调用形态全部覆盖。
    /// </summary>
    public class TenantGrpcServiceE2ETests : IClassFixture<GrpcTestServer>
    {
        private readonly GrpcTestServer _server;
        private readonly TenantGrpcService.TenantGrpcServiceClient _client;
        private readonly VivContextContent _sentContext = new() { AppId = 1001, SubjectId = 77, UserId = 5 };

        public TenantGrpcServiceE2ETests(GrpcTestServer server)
        {
            _server = server;
            var accessor = new VivContextAccessor();
            var vivContext = new VivContext(accessor);
            vivContext.SetSnapshot(_sentContext);

            var channel = GrpcChannel.ForAddress(server.Address, new GrpcChannelOptions
            {
                HttpHandler = new SocketsHttpHandler { EnableMultipleHttp2Connections = true }
            });

            _client = new TenantGrpcService.TenantGrpcServiceClient(
                channel.Intercept(new VivGrpcInterceptor(vivContext)));
        }

        [Fact]
        public async Task Unary_空请求_服务端从注入头恢复租户上下文()
        {
            var response = await _client.GetTenantAsync(new GetTenantRequest());

            Assert.True(response.Success);
            Assert.NotNull(response.Tenant);
            Assert.Equal(_sentContext.SubjectId, response.Tenant.SubjectId);
            Assert.Equal(_sentContext.AppId, response.Tenant.AppId);
            Assert.Equal(_sentContext.UserId, response.Tenant.UserId);
        }

        [Fact]
        public async Task Unary_请求显式SubjectId_优先于上下文()
        {
            var response = await _client.GetTenantAsync(new GetTenantRequest { SubjectId = 200 });

            Assert.Equal(200, response.Tenant!.SubjectId);
            // 其余身份仍来自上下文
            Assert.Equal(_sentContext.AppId, response.Tenant.AppId);
        }

        [Fact]
        public async Task ServerStreaming_产出三行用户()
        {
            var users = new List<UserInfo>();
            using var call = _client.ListTenantUsers(new ListTenantUsersRequest());
            await foreach (var user in call.ResponseStream.ReadAllAsync())
            {
                users.Add(user);
            }

            Assert.Equal(3, users.Count);
        }

        [Fact]
        public async Task ClientStreaming_累计计数返回()
        {
            using var call = _client.UploadTenantUsers();
            await call.RequestStream.WriteAsync(new UserInfo { UserId = 1, UserName = "u1" });
            await call.RequestStream.WriteAsync(new UserInfo { UserId = 2, UserName = "u2" });
            await call.RequestStream.WriteAsync(new UserInfo { UserId = 3, UserName = "u3" });
            await call.RequestStream.CompleteAsync();

            var response = await call;
            Assert.Equal(3, response.Count);
        }

        [Fact]
        public async Task DuplexStreaming_回显成立()
        {
            var replies = new List<ChatMessage>();
            using var call = _client.Chat();
            await call.RequestStream.WriteAsync(new ChatMessage { UserId = 1, Content = "hi" });
            await call.RequestStream.WriteAsync(new ChatMessage { UserId = 2, Content = "yo" });
            await call.RequestStream.CompleteAsync();

            await foreach (var message in call.ResponseStream.ReadAllAsync())
            {
                replies.Add(message);
            }

            Assert.Equal(2, replies.Count);
            Assert.Equal("echo:hi", replies[0].Content);
            Assert.Equal("echo:yo", replies[1].Content);
        }

        [Fact]
        public async Task Unary_未签名头_不恢复租户()
        {
            using var channel = GrpcChannel.ForAddress(_server.Address, new GrpcChannelOptions
            {
                HttpHandler = new SocketsHttpHandler { EnableMultipleHttp2Connections = true }
            });
            var unsigned = new TenantGrpcService.TenantGrpcServiceClient(channel);

            var response = await unsigned.GetTenantAsync(new GetTenantRequest());

            Assert.True(response.Success);
            Assert.Equal(0, response.Tenant!.AppId);
            Assert.Equal(0, response.Tenant.UserId);
            Assert.Equal(0, response.Tenant.SubjectId);
        }
    }
}
