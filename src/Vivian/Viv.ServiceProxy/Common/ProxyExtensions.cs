using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Viv.Delusion.Magic;

namespace Viv.ServiceProxy.Common
{
    public static class ProxyExtensions
    {
        /// <summary>
        /// 注册所有下游服务代理。宿主只写这一行，不需要逐个服务加注册调用：
        /// <code>builder.Services.AddVivProxies(builder.Configuration);</code>
        /// 配置在 <c>VivProxies</c> 集合节点里按服务短名覆盖；不配的服务用默认值。
        /// </summary>
        public static IServiceCollection AddVivProxies(
            this IServiceCollection services,
            IConfiguration? configuration = null)
        {
            var options = new VivProxyOptions();
            configuration?.GetSection(VivProxyOptions.SectionName).Bind(options);

            services.AddSingleton(options);

            foreach (var module in LoadModules())
            {
                module.Register(services, options.Resolve(module.Key, module.DefaultServiceName));
            }

            return services;
        }

        /// <summary>
        /// 扫实现 <see cref="IProxyModule"/> 的类型。模块都是无状态、无参构造，
        /// 加服务只需新增一个模块类，入口和配置结构都不用动。
        /// </summary>
        private static IEnumerable<IProxyModule> LoadModules()
        {
            var types = TypeScanMagic.ScanTypes<IProxyModule>()
                .Where(t => t.IsClass && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) is not null)
                .OrderBy(t => t.FullName, StringComparer.Ordinal);

            foreach (var type in types)
            {
                if (Activator.CreateInstance(type) is IProxyModule module)
                    yield return module;
            }
        }
    }
}
