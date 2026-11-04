using Microsoft.Extensions.DependencyInjection;

namespace Viv.ServiceProxy.Common
{
    /// <summary>
    /// 一个下游服务的代理模块：一个服务一个实现，<see cref="ProxyExtensions.AddVivProxies"/>
    /// 扫描到它之后用集合配置里的对应节点调用 <see cref="Register"/>。
    /// 加新服务只加一个模块类，不动注册入口和配置文件结构。
    /// </summary>
    public interface IProxyModule
    {
        /// <summary>集合节点里的 key，如 "Apex"</summary>
        string Key { get; }

        /// <summary>配置没写 ServiceName 时用的默认服务名，如 "viv-apex-api"</summary>
        string DefaultServiceName { get; }

        /// <summary>注册这个服务的 gRPC 客户端与端口实现</summary>
        void Register(IServiceCollection services, ProxyEndpointOption option);
    }
}
