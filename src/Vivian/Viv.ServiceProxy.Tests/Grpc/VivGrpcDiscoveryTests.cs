using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Echo.Grpc;
using Viv.Sandrone.Impl;
using Viv.ServiceProxy.Protos;

namespace Viv.ServiceProxy.Tests.Grpc
{
    /// <summary>
    /// gRPC 服务自动发现：配置驱动下宿主零手工接线（不手动 AddScoped + MapGrpcService&lt;T&gt;）。
    /// 验证 VivGrpcDiscovery 按 [BindServiceMethod] 基类链发现实现类，并端到端恢复租户上下文。
    /// </summary>
    public class VivGrpcDiscoveryTests
    {
        [Fact]
        public void FindServices_返回TenantGrpcService()
        {
            var services = VivGrpcDiscovery.FindServices();

            Assert.Contains(services, t => t.Name == "TenantGrpcService");
        }

        [Fact]
        public async Task 发现驱动主机_端到端恢复租户上下文()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.ConfigureKestrel(o =>
                o.ConfigureEndpointDefaults(e => e.Protocols = HttpProtocols.Http2));
            builder.WebHost.UseUrls("http://127.0.0.1:0");

            builder.Services.AddVivGrpcServer();
            builder.Services.AddSingleton<IVivContextAccessor, VivContextAccessor>();
            builder.Services.AddScoped<IVivContext, VivContext>();
            GrpcTestToken.EnsureRegistered();
            // 配置驱动接线：自动发现 + 注册，宿主不再手工 AddScoped<TenantGrpcService>()
            VivGrpcDiscovery.RegisterServices(builder.Services);

            var app = builder.Build();
            app.UseRouting();
            // 配置驱动接线：自动映射，宿主不再手工 MapGrpcService<TenantGrpcService>()
            VivGrpcDiscovery.MapServices(app);

            await app.StartAsync();
            try
            {
                var sentContext = new VivContextContent { AppId = 1001, SubjectId = 77, UserId = 5 };
                var accessor = new VivContextAccessor();
                var vivContext = new VivContext(accessor);
                vivContext.SetSnapshot(sentContext);

                var channel = GrpcChannel.ForAddress(app.Urls.First(), new GrpcChannelOptions
                {
                    HttpHandler = new SocketsHttpHandler { EnableMultipleHttp2Connections = true }
                });
                var client = new TenantGrpcService.TenantGrpcServiceClient(
                    channel.Intercept(new VivGrpcInterceptor(vivContext)));

                var response = await client.GetTenantAsync(new GetTenantRequest());

                Assert.True(response.Success);
                Assert.Equal(sentContext.SubjectId, response.Tenant!.SubjectId);
                Assert.Equal(sentContext.AppId, response.Tenant.AppId);
                Assert.Equal(sentContext.UserId, response.Tenant.UserId);
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
    }
}
