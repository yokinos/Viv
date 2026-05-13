using Microsoft.Extensions.DependencyInjection;
using Viv.Echo.Options;

namespace Viv.Echo.Grpc
{
    public static class VivGrpcExtensions
    {
        public static IHttpClientBuilder AddVivGrpcClient<TClient>(
            this IServiceCollection services,
            string address)
            where TClient : class
        {
            return services
                .AddGrpcClient<TClient>(o => o.Address = new Uri(address))
                .AddInterceptor<VivGrpcInterceptor>();
        }

        public static IServiceCollection AddVivGrpcClients(
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
