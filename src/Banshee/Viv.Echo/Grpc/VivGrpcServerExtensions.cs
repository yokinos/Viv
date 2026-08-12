using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace Viv.Echo.Grpc
{
    public static class VivGrpcServerExtensions
    {
        /// <summary>
        /// 注册框架级 gRPC 服务端（含 <see cref="VivGrpcServerInterceptor"/> 租户上下文恢复拦截器）。
        /// 配置驱动（viv.config.json <c>EchoOption.GrpcOption.EnableServer</c>）时由 <see cref="AddVivGrpcKestrel"/>
        /// 自动调用，具体业务服务经 <see cref="VivGrpcDiscovery"/> 自动发现注册 + 映射；也可手动显式调用 +
        /// <c>MapGrpcService&lt;T&gt;()</c>。
        /// </summary>
        public static void AddVivGrpcServer(this IServiceCollection services)
        {
            services.AddGrpc(o => o.Interceptors.Add(typeof(VivGrpcServerInterceptor)));
        }

        /// <summary>
        /// 专用 gRPC 端口的 Kestrel 配置：<paramref name="grpcPort"/> 严格 HTTP/2（明文 h2c），REST 端口沿用
        /// urls（--urls / ASPNETCORE_URLS / launchSettings）显式绑定为 HTTP/1.1，无 urls 时回落 Kestrel 默认 5000。
        /// 同时自动注册 gRPC 服务端（含 <see cref="VivGrpcServerInterceptor"/> 租户上下文恢复拦截器）——
        /// 声明 gRPC 端口即自动装配，宿主无需再手动调 <c>AddVivGrpcServer</c>。
        /// 配置驱动时由框架 <c>AddVivApi</c>（Viv.Engine）调用，映射由 <see cref="VivGrpcDiscovery"/> 自动完成。
        ///
        /// 为什么必须分开端口：gRPC 需要 HTTP/2，明文下 <c>Http1AndHttp2</c> 只认 TLS/ALPN，不认 h2c prior-knowledge
        /// 前缀（Grpc.Net.Client 明文即发前缀），会回 <c>HTTP_1_1_REQUIRED</c>；而严格 Http2 会把 HTTP/1.1 REST 打挂（400）。
        /// 故 REST 与 gRPC 各占一个端口。显式 <c>Listen</c> 会顶掉 urls 生成的端点（"Overriding address(es)..."），
        /// 因此 REST 端口也必须显式 Listen 回来。
        /// </summary>
        public static void AddVivGrpcKestrel(this WebApplicationBuilder builder, int grpcPort)
        {
            builder.Services.AddVivGrpcServer();

            builder.WebHost.ConfigureKestrel(o =>
            {
                o.Listen(IPAddress.Any, grpcPort, l => l.Protocols = HttpProtocols.Http2);

                var restBound = false;
                var urls = builder.Configuration["urls"] ?? builder.Configuration["ASPNETCORE_URLS"] ?? string.Empty;
                foreach (var url in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var u))
                    {
                        continue;
                    }

                    if (u.Host is "localhost" or "127.0.0.1" or "::1")
                    {
                        o.ListenLocalhost(u.Port, l => l.Protocols = HttpProtocols.Http1);
                    }
                    else
                    {
                        o.ListenAnyIP(u.Port, l => l.Protocols = HttpProtocols.Http1);
                    }

                    restBound = true;
                }

                if (!restBound)
                {
                    o.Listen(IPAddress.Any, 5000, l => l.Protocols = HttpProtocols.Http1);
                }
            });
        }
    }
}
