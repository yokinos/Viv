using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Viv.Echo.Grpc;
using Viv.ServiceProxy.Apex.Ports;
using Viv.ServiceProxy.Apex.Proxies;
using Viv.ServiceProxy.Protos;

namespace Viv.ServiceProxy.Apex
{
    /// <summary>Apex 的代理模块，注册 gRPC 客户端和端口实现</summary>
    internal sealed class ApexProxyModule : IProxyModule
    {
        public string Key => "Apex";

        public string DefaultServiceName => "viv-apex-api";

        public void Register(IServiceCollection services, ProxyEndpointOption option)
        {
            services.AddVivGrpcClient<ApexAppQuery.ApexAppQueryClient>(
                option.ServiceName!,
                option.UseServiceDiscovery,
                TimeSpan.FromSeconds(option.TimeoutSeconds));

            services.TryAddSingleton<AppCheckCache>();
            services.TryAddScoped(sp => new ApexAppServiceProxy(
                sp.GetRequiredService<ApexAppQuery.ApexAppQueryClient>(),
                sp.GetRequiredService<AppCheckCache>(),
                option,
                sp.GetRequiredService<Viv.Log.ILoggerContract>()) as IProxyApexAppService);
        }
    }
}
