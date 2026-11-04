using Microsoft.Extensions.DependencyInjection;

namespace Viv.ServiceProxy
{
    /// <summary>
    /// 一个下游服务一个模块。<see cref="ProxyExtensions.AddVivProxies"/> 扫到之后按配置节点调 Register。
    /// 新增服务只加模块类，注册入口不动。
    /// </summary>
    public interface IProxyModule
    {
        /// <summary>配置集合里的 key，如 Apex</summary>
        string Key { get; }

        /// <summary>配置没写 ServiceName 时用的服务名，如 viv-apex-api</summary>
        string DefaultServiceName { get; }

        /// <summary>注册这个服务的 gRPC 客户端和端口实现</summary>
        void Register(IServiceCollection services, ProxyEndpointOption option);
    }
}
