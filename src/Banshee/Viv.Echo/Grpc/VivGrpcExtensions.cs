using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ServiceDiscovery;

namespace Viv.Echo.Grpc
{
    public static class VivGrpcExtensions
    {
        /// <summary>
        /// 注册单个 gRPC 客户端（带 Viv 拦截器，自动注入当前租户上下文 x-viv-* 头）。
        /// <paramref name="useServiceDiscovery"/>=true 时把入参当服务名，走 Aspire 服务发现
        /// （解析 <c>services__*</c> 环境变量，AppHost WithReference 注入），否则按显式地址。
        /// </summary>
        public static IHttpClientBuilder AddVivGrpcClient<TClient>(
            this IServiceCollection services,
            string addressOrServiceName,
            bool useServiceDiscovery = false)
            where TClient : class
        {
            var builder = services.AddGrpcClient<TClient>(
                o => o.Address = new Uri(useServiceDiscovery ? $"http://{addressOrServiceName}" : addressOrServiceName));
            if (useServiceDiscovery)
            {
                builder.AddServiceDiscovery();
            }

            return builder.AddInterceptor<VivGrpcInterceptor>();
        }
    }
}
