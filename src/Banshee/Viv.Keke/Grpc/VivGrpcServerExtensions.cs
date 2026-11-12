using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace Viv.Keke.Grpc
{
    public static class VivGrpcServerExtensions
    {
        /// <summary>
        /// 注册框架级 gRPC 服务端（含 <see cref="VivGrpcServerInterceptor"/> 租户上下文恢复拦截器）。
        /// 配置驱动（appsettings.json <c>VivOptions.KekeOption.GrpcOption.Protocol</c>）时由 <see cref="AddVivGrpcKestrel"/>
        /// 自动调用，具体业务服务经 <see cref="VivGrpcDiscovery"/> 自动发现注册 + 映射；也可手动显式调用 +
        /// <c>MapGrpcService&lt;T&gt;()</c>。
        /// </summary>
        public static void AddVivGrpcServer(this IServiceCollection services)
        {
            services.AddGrpc(o => o.Interceptors.Add(typeof(VivGrpcServerInterceptor)));
        }

        /// <summary>
        /// 专用 RPC 端口的 Kestrel 配置：<paramref name="grpcPort"/> 严格 HTTP/2（明文 h2c），REST 端口沿用
        /// urls（--urls / ASPNETCORE_URLS / launchSettings）显式绑定为 HTTP/1.1（https:// 条目保留 TLS/开发证书，
        /// http:// 条目明文），无 urls 时回落 Kestrel 默认 5000。
        ///
        /// <para><paramref name="enableGrpcServer"/> 决定是否顺带注册 gRPC 服务端（含
        /// <see cref="VivGrpcServerInterceptor"/> 租户上下文恢复拦截器）。它现在是独立开关：
        /// MagicOnion 与 gRPC 共用同一条 HTTP/2 端口，协议切到 MagicOnion 时端口仍要绑，但不能注册 gRPC 服务端。
        /// 配置驱动时由框架 <c>AddVivApi</c>（Viv.Engine）按 <see cref="GrpcOptions.Protocol"/> 调用。</para>
        ///
        /// 为什么必须分开端口：gRPC 需要 HTTP/2，明文下 <c>Http1AndHttp2</c> 只认 TLS/ALPN，不认 h2c prior-knowledge
        /// 前缀（Grpc.Net.Client 明文即发前缀），会回 <c>HTTP_1_1_REQUIRED</c>；而严格 Http2 会把 HTTP/1.1 REST 打挂（400）。
        /// 故 REST 与 RPC 各占一个端口。显式 <c>Listen</c> 会顶掉 urls 生成的端点（"Overriding address(es)..."），
        /// 因此 REST 端口也必须显式 Listen 回来。
        /// </summary>
        public static void AddVivGrpcKestrel(this WebApplicationBuilder builder, int grpcPort, bool enableGrpcServer = true)
        {
            if (enableGrpcServer)
            {
                builder.Services.AddVivGrpcServer();
            }

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

                    // 显式 Listen 顶掉 urls 后必须按原 scheme 重绑：https:// 条目保留 TLS（开发证书），http:// 条目明文，
                    // 二者都强制 HTTP/1.1（REST 与 gRPC 分端口，避免明文 Http2 冲突）
                    var bind = u.Scheme == Uri.UriSchemeHttps
                        ? (Action<ListenOptions>)(l => { l.Protocols = HttpProtocols.Http1; l.UseHttps(); })
                        : l => { l.Protocols = HttpProtocols.Http1; };

                    if (u.Host is "localhost" or "127.0.0.1" or "::1")
                    {
                        o.ListenLocalhost(u.Port, bind);
                    }
                    else
                    {
                        o.ListenAnyIP(u.Port, bind);
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
