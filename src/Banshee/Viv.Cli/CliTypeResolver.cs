using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace Viv.Cli
{
    /// <summary>
    /// 解析命令与 Spectre 自带的组件。先查登记器存下的那份，其余落宿主容器，
    /// 命令就是从宿主容器出去的，所以注入得到 IMomoDbContext 这类服务。
    ///
    /// 实例由 CliTypeRegistrar 持有并复用，Spectre 每次 RunAsync 都调一次 Build()。
    ///
    /// 作用域的开合由 VivCliHost 在每条命令前后调 BeginScope / EndScope，命令之间的
    /// Scoped 服务才不会串。
    /// </summary>
    public sealed class CliTypeResolver : ITypeResolver, IDisposable
    {
        private readonly Dictionary<Type, Func<IServiceProvider?, object?>> _registrations = [];

        private IServiceScope? _scope;

        /// <summary>宿主容器，由 VivCliHost.UseContainer 接上；没接上时什么都解析不出来。</summary>
        public IServiceProvider? Provider { get; set; }

        /// <summary>记下 Spectre 要的登记。</summary>
        internal void Add(Type service, Func<IServiceProvider?, object?> factory)
            => _registrations[service] = factory;

        public void BeginScope()
        {
            EndScope();
            _scope = Provider?.CreateScope();
        }

        public void EndScope()
        {
            _scope?.Dispose();
            _scope = null;
        }

        public object? Resolve(Type? type)
        {
            if (type == null)
            {
                return null;
            }

            var provider = _scope?.ServiceProvider ?? Provider;

            if (_registrations.TryGetValue(type, out var factory))
            {
                return factory(provider);
            }

            return provider?.GetService(type);
        }

        /// <summary>只收掉自己那一层作用域，宿主的容器不归它管。</summary>
        public void Dispose() => EndScope();
    }
}
