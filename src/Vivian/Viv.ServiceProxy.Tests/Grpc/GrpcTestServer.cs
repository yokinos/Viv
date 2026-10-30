using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Viv.Contracts;
using Viv.Contracts.Interface;
using Viv.Echo.Grpc;
using Viv.Sandrone.Impl;

namespace Viv.ServiceProxy.Tests.Grpc
{
    /// <summary>
    /// 真实 Kestrel in-process gRPC 服务端（随机端口，HTTP/2），复用框架级
    /// AddVivGrpcServer（含租户上下文恢复拦截器）+ 示例 TenantGrpcStub 作实现桩。
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
            builder.Services.AddScoped<TenantGrpcStub>();
            builder.Services.AddSingleton(GrpcTestToken.Options);

            _app = builder.Build();
            // 生产管道由 VivContextMiddleware / RequestTrackMiddleware 在本地事件分发之后清上下文。
            // 测试宿主没有那两层，这里在端点外面清，保证处理期间拦截器灌进去的租户还在。
            _app.UseOuterContextCleanup();
            _app.UseRouting();
            _app.MapGrpcService<TenantGrpcStub>();

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

    internal static class GrpcTestPipeline
    {
        public static void UseOuterContextCleanup(this WebApplication app)
        {
            app.Use(async (HttpContext ctx, RequestDelegate next) =>
            {
                try
                {
                    await next(ctx);
                }
                finally
                {
                    ctx.RequestServices.GetService<IVivContext>()?.Clear();
                    LockHolderContext.Clear();
                }
            });
        }
    }
}
