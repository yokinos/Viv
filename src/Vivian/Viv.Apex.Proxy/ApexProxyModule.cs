using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Viv.Apex.Proxy.Contracts;
using Viv.Apex.Proxy.Ports;
using Viv.Apex.Proxy.Proxies;
using Viv.Echo.MagicOnion;
using Viv.Echo.Proxies;

namespace Viv.Apex.Proxy
{
    /// <summary>
    /// Apex 的下游代理模块：一个下游服务一个模块，<see cref="VivRpcProxyExtensions.AddVivRpcProxies"/>
    /// 扫到后按 <c>VivProxies:Apex</c> 节点绑定配置再 Register。
    /// </summary>
    internal sealed class ApexProxyModule : IVivRpcProxyModule
    {
        public string Key => "Apex";

        public string DefaultServiceName => "viv-apex-api";

        public void Register(IServiceCollection services, VivRpcClientOption option)
        {
            // 身份头过滤器（全局单例）。幂等：多个模块调也只注册一次。
            services.AddVivMagicOnionClient();

            // MagicOnion 客户端是线程安全的，channel 复用，按单例注册。
            services.TryAddSingleton(sp => sp.CreateClient<IApexAppQueryService>(option));

            services.TryAddScoped<IProxyApexAppService, ApexAppQueryProxy>();
        }
    }
}
