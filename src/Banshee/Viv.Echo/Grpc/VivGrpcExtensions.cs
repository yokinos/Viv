using Microsoft.Extensions.DependencyInjection;

namespace Viv.Echo.Grpc
{
    public static class VivGrpcExtensions
    {
        /// <summary>
        /// 注册单个 gRPC 客户端（带 Viv 拦截器）
        /// </summary>
        public static IHttpClientBuilder AddVivGrpcClient<TClient>(
            this IServiceCollection services,
            string address)
            where TClient : class
        {
            return services
                .AddGrpcClient<TClient>(o => o.Address = new Uri(address))
                .AddInterceptor<VivGrpcInterceptor>();
        }
    }
}
