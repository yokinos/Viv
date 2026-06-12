using Microsoft.Extensions.DependencyInjection;
using Viv.Echo.Options;

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

        /// <summary>
        /// 旧方式：从配置反射注册 gRPC 客户端（仍可用）。
        /// 推荐使用 <see cref="GrpcClientAttribute"/> 标注接口，编译时自动生成。
        /// </summary>
        public static IServiceCollection AddVivGrpcClientsFromConfig(
            this IServiceCollection services,
            EchoOptions options)
        {
            if (options.GrpcEndpoints.Count == 0) return services;

            foreach (var endpoint in options.GrpcEndpoints)
            {
                var clientType = Type.GetType($"{endpoint.ClientTypeFullName}, {endpoint.ClientTypeAssembly}");
                if (clientType == null) continue;

                var method = typeof(VivGrpcExtensions)
                    .GetMethod(nameof(AddVivGrpcClient))
                    ?.MakeGenericMethod(clientType);

                method?.Invoke(null, [services, endpoint.Address]);
            }

            return services;
        }
    }
}
