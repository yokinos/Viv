using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace Viv.Aoi.Paradox
{
    /// <summary>
    /// 把 <see cref="IVivContainer"/> 包装成一个标准的 MS DI 容器，给只认
    /// <see cref="IServiceProvider"/> 那一套接口的地方用（ASP.NET Core 的宿主、通用主机、
    /// 各种第三方库）。
    /// </summary>
    /// <remarks>
    /// 容器本体已经实现了这些接口，这个包装存在的理由是 <see cref="IServiceScopeFactory"/>：
    /// 它需要 <c>IServiceScope CreateScope()</c>，而容器自己那个 <c>CreateScope</c> 返回更窄的
    /// <see cref="IVivScope"/>，C# 不允许靠返回类型协变去隐式实现接口成员（CS0738），
    /// 只能在另一个类型上补。顺带也把「容器」与「容器提供的服务」这两件事在类型上分了开。
    ///
    /// 这里返回的作用域是容器原生的 <see cref="IVivScope"/>，没有再包一层 ——
    /// 它本身就是完整的 <see cref="IServiceScope"/>，再套一层只是白加一个对象。
    ///
    /// 这个包装持有被包装容器的所有权：<see cref="Dispose"/> 会一并把它释放掉。
    /// </remarks>
    public sealed class ServiceProviderVivContainer : IServiceProvider, IServiceProviderIsService, ISupportRequiredService, IServiceScopeFactory, IServiceScope, IAsyncDisposable
    {
        private readonly IVivContainer _container;

        /// <summary>
        /// 包装一个容器。
        /// </summary>
        /// <param name="container">被包装的容器，由这个包装接管释放。</param>
        public ServiceProviderVivContainer(IVivContainer container)
        {
            ArgumentNullException.ThrowIfNull(container);
            _container = container;
        }

        /// <inheritdoc />
        public object? GetService(Type serviceType) => _container.GetService(serviceType);

        /// <inheritdoc />
        public bool IsService(Type serviceType) => _container.IsService(serviceType);

        /// <inheritdoc />
        public object GetRequiredService(Type serviceType) => _container.GetRequiredService(serviceType);

        /// <inheritdoc />
        public IServiceScope CreateScope() => _container.CreateScope();

        /// <inheritdoc />
        IServiceProvider IServiceScope.ServiceProvider => this;

        /// <inheritdoc />
        public void Dispose() => _container.Dispose();

        /// <inheritdoc />
        public ValueTask DisposeAsync() => _container.DisposeAsync();
    }
}
