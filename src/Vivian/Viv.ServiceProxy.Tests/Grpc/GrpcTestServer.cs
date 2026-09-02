using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Viv.Contracts.Interface;
using Viv.Echo.Grpc;
using Viv.Sandrone.Impl;
using Viv.ServiceProxy.Examples;

namespace Viv.ServiceProxy.Tests.Grpc
{
    /// <summary>
    /// 真实 Kestrel in-process gRPC 服务端（随机端口，HTTP/2），复用框架级
    /// AddVivGrpcServer（含租户上下文恢复拦截器）+ 示例 TenantGrpcService 作实现桩。
    /// </summary>
    public sealed class GrpcTestServer : IAsyncLifetime
    {
        private WebApplication? _app;

        /// <summary>已绑定地址，如 http://127.0.0.1:51234</summary>
        public string Address { get; private set; } = string.Empty;

        public async Task InitializeAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.ConfigureKestrel(o =>
                o.ConfigureEndpointDefaults(e => e.Protocols = HttpProtocols.Http2));
            builder.WebHost.UseUrls("http://127.0.0.1:0");

            builder.Services.AddVivGrpcServer();
            builder.Services.AddSingleton<IVivContextAccessor, VivContextAccessor>();
            builder.Services.AddScoped<IVivContext, VivContext>();
            builder.Services.AddScoped<TenantGrpcService>();
            GrpcTestToken.EnsureRegistered();

            _app = builder.Build();
            _app.UseRouting();
            _app.MapGrpcService<TenantGrpcService>();

            await _app.StartAsync();
            Address = _app.Urls.First();
        }

        public async Task DisposeAsync()
        {
            if (_app is null)
            {
                return;
            }

            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
