using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace Viv.Cli
{
    /// <summary>
    /// 收下 Spectre 要登记的东西，转交给解析器。
    ///
    /// 这些登记不写宿主注册表。宿主的注册表在 Build 那一刻就封了，而 Spectre 每跑一条命令
    /// 都要登记一次 IConfiguration，写进去会抛。命令类型走的是另一条路。
    /// </summary>
    public sealed class CliTypeRegistrar : ITypeRegistrar
    {
        private readonly CliTypeResolver _resolver = new();

        public CliTypeResolver Resolver => _resolver;

        public ITypeResolver Build() => _resolver;

        public void Register(Type service, Type implementation)
            => _resolver.Add(service, provider => provider == null
                ? null
                : ActivatorUtilities.CreateInstance(provider, implementation));

        public void RegisterInstance(Type service, object implementation)
            => _resolver.Add(service, _ => implementation);

        public void RegisterLazy(Type service, Func<object> factory)
            => _resolver.Add(service, _ => factory());
    }
}
