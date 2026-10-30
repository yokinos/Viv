using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ServiceDiscovery;

namespace Viv.Echo.Grpc
{
    public static class VivGrpcExtensions
    {
        /// <summary>
        /// Aspire 里 gRPC 端点的名字。客户端发现地址是 <c>http://_grpc.{服务名}</c>，
        /// 网关解析 <c>services__*</c> 时要跳过同名段，避免把这端口当成 REST。
        /// </summary>
        public const string ServiceDiscoveryEndpointName = "grpc";

        /// <summary>
        /// 注册单个 gRPC 客户端（带 Viv 拦截器，自动注入当前租户上下文 x-viv-* 头）。
        /// <paramref name="useServiceDiscovery"/>=true 时把入参当服务名，走 Aspire 服务发现，
        /// 地址是 <c>http://_grpc.{服务名}</c>（选名为 <see cref="ServiceDiscoveryEndpointName"/> 的端点，
        /// 不是项目的 HTTP/1.1 REST 地址）。否则按显式地址。
        /// </summary>
        public static IHttpClientBuilder AddVivGrpcClient<TClient>(
            this IServiceCollection services,
            string addressOrServiceName,
            bool useServiceDiscovery = false)
            where TClient : class
        {
            var builder = services.AddGrpcClient<TClient>(o => o.Address = new Uri(
                useServiceDiscovery
                    ? $"http://_{ServiceDiscoveryEndpointName}.{addressOrServiceName}"
                    : addressOrServiceName));
            if (useServiceDiscovery)
            {
                builder.AddServiceDiscovery();
            }

            return builder.AddInterceptor<VivGrpcInterceptor>();
        }
    }
}
