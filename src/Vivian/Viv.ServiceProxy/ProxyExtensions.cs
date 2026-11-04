using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Viv.Delusion.Magic;

namespace Viv.ServiceProxy
{
    public static class ProxyExtensions
    {
        /// <summary>
        /// 注册所有下游服务代理，宿主写一行：
        /// <code>builder.Services.AddVivProxies(builder.Configuration);</code>
        /// 配置在 VivProxies 节点里按服务短名覆盖，不配就用默认值。
        /// </summary>
        public static IServiceCollection AddVivProxies(this IServiceCollection services, IConfiguration? configuration = null)
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

        /// <summary>扫 IProxyModule 实现。模块无状态、无参构造，加服务只加类</summary>
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
