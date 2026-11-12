using Grpc.Net.Client;
using MagicOnion;
using MagicOnion.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Viv.Keke.Proxies;
using Viv.Keke.MagicOnion.Filters;

namespace Viv.Keke.MagicOnion
{
    /// <summary>
    /// MagicOnion 客户端装配。与 gRPC 客户端共用同一套约定：
    /// 服务发现地址是 <c>http://_grpc.{服务名}</c>（<see cref="Grpc.VivGrpcExtensions.ServiceDiscoveryEndpointName"/>），
    /// 超时来自 <see cref="VivRpcClientOption"/>，身份头由全局客户端过滤器注入。
    ///
    /// <para>和 gRPC 的 <c>AddVivGrpcClient&lt;T&gt;</c> 不同，MagicOnion 的调用入口是
    /// <see cref="MagicOnionClient.Create{T}"/>，拿的是 <see cref="GrpcChannel"/> 而不是生成的 typed client，
    /// 所以这里自己建 channel —— 但 HttpMessageHandler 仍走 <c>IHttpClientFactory</c>，
    /// 从而复用 <c>AddServiceDefaults</c> 注册的服务发现与弹性管道。</para>
    /// </summary>
    public static class VivMagicOnionClientExtensions
    {
        /// <summary>服务发现端点名前缀：与 <c>WithVivGrpcEndpoint</c> 声明的端点名必须一致。</summary>
        public const string ServiceDiscoveryPrefix = "_grpc.";

        /// <summary>
        /// 注册全局客户端过滤器（身份头）。宿主只调一次。
        /// </summary>
        public static IServiceCollection AddVivMagicOnionClient(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.TryAddSingleton<IClientFilter, VivIdentityClientFilter>();
            return services;
        }

        /// <summary>
        /// 按配置创建 MagicOnion 客户端代理。由各服务的代理包（<c>Viv.Xxx.Proxy</c> 的
        /// <c>IVivRpcProxyModule</c> 实现）在自己的 <c>Register</c> 里调用。
        /// </summary>
        public static T CreateClient<T>(this IServiceProvider services, VivRpcClientOption option)
            where T : class, IService<T>
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(option);

            var filters = services.GetServices<IClientFilter>().ToArray();
            return MagicOnionClient.Create<T>(CreateChannel(services, option), filters);
        }

        /// <summary>
        /// 建 <see cref="GrpcChannel"/>：地址按是否走服务发现拼 <c>http://_grpc.{服务名}</c>，
        /// 处理器来自 <see cref="IHttpClientFactory"/>（因此带服务发现与标准弹性策略）。
        /// </summary>
        public static GrpcChannel CreateChannel(IServiceProvider services, VivRpcClientOption option)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(option);

            var http = services.GetRequiredService<IHttpClientFactory>().CreateClient();
            http.Timeout = option.Timeout;

            var address = option.UseServiceDiscovery
                ? $"http://{ServiceDiscoveryPrefix}{option.ServiceName}"
                : option.ServiceName!;

            return GrpcChannel.ForAddress(new Uri(address), new GrpcChannelOptions
            {
                HttpClient = http
            });
        }
    }
}
