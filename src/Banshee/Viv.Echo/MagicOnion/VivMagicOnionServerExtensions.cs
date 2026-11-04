using MagicOnion;
using MagicOnion.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Viv.Echo.MagicOnion.Filters;

namespace Viv.Echo.MagicOnion
{
    /// <summary>
    /// MagicOnion 服务端装配。与 gRPC 宿主共用同一条严格 HTTP/2 明文端口
    /// （端口由 <c>VivOptions.EchoOption.GrpcOption.Port</c> 决定，由 <c>AddVivGrpcKestrel</c> 绑定），
    /// 只是服务路径不同。
    ///
    /// <para>迁移期服务端用 <c>Protocol = Both</c>：gRPC 与 MagicOnion 同时映射，
    /// 客户端逐条服务切换时不会因为“服务端只开了一边”而整条链路失败。</para>
    ///
    /// <para>用法（宿主是 Apex.Api 这种同时对外提供 RPC 的服务）：
    /// <code>
    /// builder.Services.AddVivMagicOnionServer();          // AddVivApi 之后
    /// builder.RunVivApi(app =&gt; { app.MapVivMagicOnionServices(); });
    /// </code>
    /// </para>
    /// </summary>
    public static class VivMagicOnionServerExtensions
    {
        /// <summary>
        /// 注册 MagicOnion 服务端，并把身份头过滤器挂成**全局**过滤器。
        /// 全局是硬要求：身份/签名校验漏挂不会报错，只会让 <c>IVivContext</c> 静默为空。
        /// </summary>
        public static IServiceCollection AddVivMagicOnionServer(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddMagicOnion(options =>
            {
                options.GlobalFilters.Add<VivIdentityFilter>();
            });

            return services;
        }

        /// <summary>
        /// 映射所有 MagicOnion 服务（MagicOnion 自己按程序集发现 <c>IService&lt;T&gt;</c> 的实现）。
        /// 必须在 <c>app.Build()</c> 之后调用 —— 即 <c>RunVivApi</c> 的 configure 回调里。
        /// </summary>
        public static IEndpointRouteBuilder MapVivMagicOnionServices(this IEndpointRouteBuilder endpoints)
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            endpoints.MapMagicOnionService();
            return endpoints;
        }
    }
}
