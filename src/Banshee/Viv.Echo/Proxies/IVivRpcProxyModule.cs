using Microsoft.Extensions.DependencyInjection;

namespace Viv.Echo.Proxies
{
    /// <summary>
    /// 一个下游服务一个代理模块（如 <c>ApexProxyModule</c>）。<see cref="VivRpcProxyExtensions.AddVivRpcProxies"/>
    /// 会扫出所有实现，按 <see cref="Key"/> 绑 <c>VivProxies:&lt;Key&gt;</c> 后逐个调 <see cref="Register"/>。
    ///
    /// 扫描范围 = 宿主引用到的程序集闭包：引了 <c>Viv.Apex.Proxy</c> 才会注册 Apex 的客户端，
    /// 不引就扫不到 —— 这是“按服务拆包”之后，模块扫描不再变成“全量注册”的原因。
    ///
    /// <para>约定：模块类必须 <c>public</c> + 无参构造；<see cref="Register"/> 只做 DI 登记
    /// （客户端 + 端口实现），**不要**放 IO、健康探测、后台任务这类副作用 —— 它会在每个引用该包的宿主里执行。</para>
    /// </summary>
    public interface IVivRpcProxyModule
    {
        /// <summary>配置节点名，如 <c>Apex</c>（<c>VivProxies:Apex</c>）。</summary>
        string Key { get; }

        /// <summary>配置没写 <see cref="VivRpcClientOption.ServiceName"/> 时用的服务名，如 <c>viv-apex-api</c>。</summary>
        string DefaultServiceName { get; }

        /// <summary>注册这个下游服务的客户端与端口实现。</summary>
        void Register(IServiceCollection services, VivRpcClientOption option);
    }
}
