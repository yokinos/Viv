using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Viv.Delusion.Magic;

namespace Viv.Echo.Proxies
{
    /// <summary>
    /// 下游代理的注册入口：扫 <see cref="IVivRpcProxyModule"/>，逐模块绑定配置后注册。
    ///
    /// <para>与旧 <c>Viv.ServiceProxy.AddVivProxies</c> 的差别：</para>
    /// <list type="bullet">
    /// <item><description>配置按模块**直接**绑定 <c>VivProxies:&lt;Key&gt;</c>，不再经过字典包装
    /// （旧写法 Binder 绑不上，配置实际是死的）。</description></item>
    /// <item><description>扫描前强制加载引用程序集：否则模块在启动早期还没被加载，
    /// 扫描会静默返回 0 个（旧实现依赖“恰好先调过 AddVivApi”这一顺序）。</description></item>
    /// <item><description>模块实例以 <see cref="IVivRpcProxyModule"/> 登记进 DI，宿主可注入
    /// <c>IEnumerable&lt;IVivRpcProxyModule&gt;</c> 查看“这台机器注册了哪些下游”。</description></item>
    /// </list>
    /// </summary>
    public static class VivRpcProxyExtensions
    {
        /// <summary>配置集合节点名，与旧约定保持一致。</summary>
        public const string SectionName = "VivProxies";

        /// <summary>
        /// 注册所有可发现的下游代理。宿主只写这一行：
        /// <code>builder.Services.AddVivRpcProxies(builder.Configuration);</code>
        /// </summary>
        public static IServiceCollection AddVivRpcProxies(
            this IServiceCollection services,
            IConfiguration? configuration = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            // 模块所在程序集可能尚未加载（业务包通常只被引用、没被直接触碰），
            // 不先强制加载就会静默漏扫。
            TypeScanMagic.ForceLoadReferencedAssemblies();

            foreach (var moduleType in FindModules())
            {
                var module = (IVivRpcProxyModule)Activator.CreateInstance(moduleType)!;
                services.AddSingleton(module);
                module.Register(services, BindOption(configuration, module));
            }

            return services;
        }

        /// <summary>扫出所有可实例化的模块（public、非抽象、有公共无参构造），按全名排序保证注册顺序稳定。</summary>
        public static IReadOnlyList<Type> FindModules()
        {
            return TypeScanMagic.ScanTypes<IVivRpcProxyModule>()
                .Where(type => type.IsClass && !type.IsAbstract)
                .Where(type => type.GetConstructor(Type.EmptyTypes) is not null)
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// 绑定单个模块的配置并补默认值。服务名回落模块声明的默认值，超时/缓存做下限保护。
        /// </summary>
        public static VivRpcClientOption BindOption(IConfiguration? configuration, IVivRpcProxyModule module)
        {
            ArgumentNullException.ThrowIfNull(module);

            var option = new VivRpcClientOption();
            configuration?
                .GetSection($"{SectionName}:{module.Key}")
                .Bind(option);

            if (string.IsNullOrWhiteSpace(option.ServiceName))
            {
                option.ServiceName = module.DefaultServiceName;
            }

            if (option.TimeoutSeconds <= 0)
            {
                option.TimeoutSeconds = 30;
            }

            return option;
        }
    }
}
