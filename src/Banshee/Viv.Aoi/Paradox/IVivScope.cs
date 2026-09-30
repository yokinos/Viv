using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Viv.Aoi.Paradox
{
    /// <summary>
    /// 表示一个 IOC 作用域，用于解析 Scoped 生命周期服务。
    /// </summary>
    /// <remarks>
    /// 作用域由 <see cref="IVivContainer.CreateScope"/> 创建，每个作用域内部维护独立的
    /// Scoped 服务实例缓存，不同作用域之间互不共享。
    ///
    /// 作用域解析 Scoped 服务时会使用自身缓存；解析 Singleton 服务时复用根容器缓存；
    /// 解析 Transient 服务时每次都创建新实例。
    ///
    /// 调用方负责释放作用域，推荐使用 <c>using</c> 或 <c>await using</c> 语法，
    /// 释放时会依次释放作用域内所有实现了 <see cref="IDisposable"/> 或
    /// <see cref="IAsyncDisposable"/> 的 Scoped 实例。
    ///
    /// 这个接口同时是 <see cref="IServiceProvider"/>、<see cref="IServiceProviderIsService"/>、
    /// <see cref="ISupportRequiredService"/> 与 <see cref="IServiceScope"/>，可以直接当作
    /// MS DI 的作用域交给第三方库（ASP.NET Core 的 <c>RequestServices</c>、<c>ActivatorUtilities</c> 等）。
    /// 它不实现 <see cref="IServiceScopeFactory"/>，那一个由 <see cref="IVivContainer"/> 承担 ——
    /// 两边的 CreateScope 返回类型不同，而 C# 的返回类型协变不支持接口实现，只能各归各位。
    /// </remarks>
    public interface IVivScope : IServiceProvider, IServiceProviderIsService, ISupportRequiredService, IServiceScope, IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// 解析指定契约类型的服务实例。
        /// </summary>
        /// <typeparam name="TContract">要解析的服务契约类型。</typeparam>
        /// <returns>解析得到的服务实例。</returns>
        /// <exception cref="InvalidOperationException">
        /// 当 <typeparamref name="TContract"/> 未注册或无法解析时抛出。
        /// </exception>
        /// <remarks>
        /// 这是必须成功的解析方式，未注册时应抛出异常而非返回 <c>null</c>。
        /// 若依赖为可选的，请使用 <see cref="TryGetService{TContract}"/>。
        /// </remarks>
        TContract GetService<TContract>();

        /// <summary>
        /// 解析指定运行时类型的服务实例。
        /// </summary>
        /// <param name="serviceType">要解析的服务类型。</param>
        /// <returns>解析得到的服务实例；若未注册则返回 <c>null</c>。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="serviceType"/> 为 <c>null</c> 时抛出。</exception>
        /// <remarks>
        /// 用于反射或框架集成场景，不会因未注册而抛出异常。本作用域没注册时转交给
        /// 建容器时传入的 fallback（若有），fallback 也没有才返回 <c>null</c>。
        ///
        /// new 只是压掉 CS0108：签名与继承来的 <see cref="IServiceProvider.GetService"/>
        /// 完全一样，这里重写一遍是为了挂这段说明，实现方一个方法同时满足两者。
        /// </remarks>
        new object? GetService(Type serviceType);

        /// <summary>
        /// 尝试解析指定契约类型的服务实例，不抛出未注册异常。
        /// </summary>
        /// <typeparam name="TContract">要解析的服务契约类型。</typeparam>
        /// <param name="service">解析成功时返回服务实例，失败时为 <c>null</c>。</param>
        /// <returns>解析成功返回 <c>true</c>；未注册时返回 <c>false</c>。</returns>
        /// <remarks>
        /// 只有「没注册」这一种情况返回 <c>false</c>。注册了但构造不出来（依赖缺失、
        /// 没有公共构造函数、循环依赖）仍然抛异常 —— 那些是真缺陷，吞掉只会变成更难查的
        /// 空引用。适合可选的依赖场景；若依赖为必需，请使用 <see cref="GetService{TContract}"/>。
        /// </remarks>
        bool TryGetService<TContract>([NotNullWhen(true)] out TContract? service);
    }
}
