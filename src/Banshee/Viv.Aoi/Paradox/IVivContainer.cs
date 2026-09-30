using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Viv.Aoi.Paradox;

/// <summary>
/// Viv 内置的 IOC 容器。一般来说非必要不需要使用这个。
/// </summary>
/// <remarks>
/// 提供三种生命周期：单例（Singleton）、作用域（Scoped）、瞬时（Transient）。
/// 通过 <see cref="CreateScope"/> 创建作用域来解析 Scoped 服务。
///
/// 同一个契约只登记一个实现，重复注册默认抛异常。注册方法上的 allowOverride 传 true 可以让
/// 后来的顶掉先前的，但那属于装配期的动作：契约一旦产生过实例就覆盖不动了（解析先撞上缓存
/// 里那份），所以那种情况直接抛，不留下「覆盖了却没生效」的静默失效。
///
/// 凡是能在注册期判定的错误一律当场抛，不留到解析时才炸：实现与契约之间没有实现关系、
/// 实现是接口或抽象类（<see cref="Type"/> 那条路编译期拦不住），以及在容器自身的契约上
/// 注册 —— <see cref="IServiceProvider"/>、<see cref="IServiceScope"/>、<see cref="IVivScope"/>、
/// <see cref="IVivContainer"/> 这些解析时直接给实例、压根不看注册表，注册进去永远不会生效。
///
/// 根容器本身也是一层合法的作用域，所以从根解析 Scoped 是允许的，拿到的是一个活得和容器
/// 一样久的实例。要拒掉这条路（<see cref="VivContainer"/> 构造参数 <c>validateScopes</c>，
/// 对应 MS DI 的 <c>ValidateScopes</c>，默认关）的场景是：从根解析的 Transient 依赖了 Scoped，
/// 而那个 Transient 又被某个单例或静态字段留了下来，那个 Scoped 就事实变成了单例。
/// </remarks>
public interface IVivContainer : IServiceProvider, IServiceProviderIsService, ISupportRequiredService, IServiceScope, IDisposable, IAsyncDisposable
{
    /// <summary>
    /// 注册单例服务，使用泛型指定契约与实现类型。
    /// </summary>
    /// <typeparam name="TContract">服务契约类型（通常为接口或抽象类）。</typeparam>
    /// <typeparam name="TImpl">服务的具体实现类型，必须可赋值给 <typeparamref name="TContract"/>。</typeparam>
    /// <param name="allowOverride">是否允许顶掉该契约上先前的注册，默认 <c>false</c>（重复注册抛异常）。</param>
    /// <remarks>
    /// 首次解析时创建实例，之后在整个容器生命周期内复用同一个实例。
    /// </remarks>
    void AddSingleton<TContract, TImpl>(bool allowOverride = false) where TImpl : class, TContract;

    /// <summary>
    /// 注册单例服务，使用运行时 <see cref="Type"/> 指定契约与实现类型。
    /// </summary>
    /// <param name="contract">服务契约类型。</param>
    /// <param name="impl">服务的具体实现类型，必须可赋值给 <paramref name="contract"/>。</param>
    /// <param name="allowOverride">是否允许顶掉该契约上先前的注册，默认 <c>false</c>（重复注册抛异常）。</param>
    /// <remarks>
    /// 用于无法使用泛型的反射或动态注册场景。
    /// </remarks>
    void AddSingleton(Type contract, Type impl, bool allowOverride = false);

    /// <summary>
    /// 注册单例服务，直接使用已创建的实例。
    /// </summary>
    /// <typeparam name="TContract">服务契约类型。</typeparam>
    /// <param name="instance">要注册的实例。为 <c>null</c> 时应抛出 <see cref="ArgumentNullException"/>。</param>
    /// <param name="allowOverride">是否允许顶掉该契约上先前的注册，默认 <c>false</c>（重复注册抛异常）。</param>
    /// <remarks>
    /// 该实例由容器持有，容器释放时若实现 <see cref="IDisposable"/> 或 <see cref="IAsyncDisposable"/> 会被一并释放。
    /// 被顶掉时旧实例仍在容器的释放清单里 —— 登记那刻所有权就转移了。
    /// </remarks>
    void AddSingleton<TContract>(TContract instance, bool allowOverride = false) where TContract : class;

    /// <summary>
    /// 注册单例服务，使用工厂委托延迟创建实例。
    /// </summary>
    /// <typeparam name="TContract">服务契约类型。</typeparam>
    /// <param name="factory">工厂委托，接收当前容器作为参数并返回服务实例。</param>
    /// <param name="allowOverride">是否允许顶掉该契约上先前的注册，默认 <c>false</c>（重复注册抛异常）。</param>
    /// <remarks>
    /// 工厂仅在首次解析时调用一次，其结果会被缓存复用。
    /// </remarks>
    void AddSingleton<TContract>(Func<IVivContainer, TContract> factory, bool allowOverride = false) where TContract : class;

    /// <summary>
    /// 注册单例服务，契约就是实现类型自身。
    /// </summary>
    /// <typeparam name="TImpl">服务的具体实现类型，同时用作契约。</typeparam>
    /// <remarks>
    /// 省掉把同一个类型写两遍。约束只有 <c>class</c>，没有双参版那条可赋值性要求 ——
    /// 契约本来就等于实现自己。
    ///
    /// 这一组刻意没有 <c>allowOverride</c>：契约就是实现自己，覆盖等于用同一个类型顶掉
    /// 同一个契约，没有语义可表。要换实现必然换契约，那就走双参版那条路。
    /// </remarks>
    void AddSingleton<TImpl>() where TImpl : class;

    /// <summary>
    /// 注册单例服务，契约就是实现类型自身，使用运行时 <see cref="Type"/>。
    /// </summary>
    /// <param name="impl">服务的具体实现类型，同时用作契约。</param>
    /// <remarks>
    /// 用于无法使用泛型的反射或动态注册场景。
    /// </remarks>
    void AddSingleton(Type impl);

    /// <summary>
    /// 注册作用域服务，使用泛型指定契约与实现类型。
    /// </summary>
    /// <typeparam name="TContract">服务契约类型。</typeparam>
    /// <typeparam name="TImpl">服务的具体实现类型，必须可赋值给 <typeparamref name="TContract"/>。</typeparam>
    /// <param name="allowOverride">是否允许顶掉该契约上先前的注册，默认 <c>false</c>（重复注册抛异常）。</param>
    /// <remarks>
    /// 每个 <see cref="IVivScope"/> 内最多创建一个实例，不同作用域间互不共享。
    /// </remarks>
    void AddScoped<TContract, TImpl>(bool allowOverride = false) where TImpl : class, TContract;

    /// <summary>
    /// 注册作用域服务，使用运行时 <see cref="Type"/> 指定契约与实现类型。
    /// </summary>
    /// <param name="contract">服务契约类型。</param>
    /// <param name="impl">服务的具体实现类型，必须可赋值给 <paramref name="contract"/>。</param>
    /// <param name="allowOverride">是否允许顶掉该契约上先前的注册，默认 <c>false</c>（重复注册抛异常）。</param>
    /// <remarks>
    /// 用于无法使用泛型的反射或动态注册场景。
    /// </remarks>
    void AddScoped(Type contract, Type impl, bool allowOverride = false);

    /// <summary>
    /// 注册作用域服务，使用工厂委托延迟创建实例。
    /// </summary>
    /// <typeparam name="TContract">服务契约类型。</typeparam>
    /// <param name="factory">工厂委托，接收所属作用域的容器作为参数并返回服务实例。</param>
    /// <param name="allowOverride">是否允许顶掉该契约上先前的注册，默认 <c>false</c>（重复注册抛异常）。</param>
    /// <remarks>
    /// 每个作用域内工厂至多调用一次，其结果在该作用域内被缓存复用。
    /// 作用域内的实例缓存根容器看不见，所以这一档的覆盖同样只在建作用域之前有意义。
    /// </remarks>
    void AddScoped<TContract>(Func<IVivContainer, TContract> factory, bool allowOverride = false) where TContract : class;

    /// <summary>
    /// 注册作用域服务，契约就是实现类型自身。
    /// </summary>
    /// <typeparam name="TImpl">服务的具体实现类型，同时用作契约。</typeparam>
    /// <remarks>
    /// 省掉把同一个类型写两遍。约束只有 <c>class</c>，没有双参版那条可赋值性要求 ——
    /// 契约本来就等于实现自己。
    ///
    /// 这一组刻意没有 <c>allowOverride</c>：契约就是实现自己，覆盖等于用同一个类型顶掉
    /// 同一个契约，没有语义可表。要换实现必然换契约，那就走双参版那条路。
    /// </remarks>
    void AddScoped<TImpl>() where TImpl : class;

    /// <summary>
    /// 注册作用域服务，契约就是实现类型自身，使用运行时 <see cref="Type"/>。
    /// </summary>
    /// <param name="impl">服务的具体实现类型，同时用作契约。</param>
    /// <remarks>
    /// 用于无法使用泛型的反射或动态注册场景。
    /// </remarks>
    void AddScoped(Type impl);

    /// <summary>
    /// 注册瞬时服务，使用泛型指定契约与实现类型。
    /// </summary>
    /// <typeparam name="TContract">服务契约类型。</typeparam>
    /// <typeparam name="TImpl">服务的具体实现类型，必须可赋值给 <typeparamref name="TContract"/>。</typeparam>
    /// <param name="allowOverride">是否允许顶掉该契约上先前的注册，默认 <c>false</c>（重复注册抛异常）。</param>
    /// <remarks>
    /// 每次解析都会创建新的实例，由调用方负责释放。
    /// </remarks>
    void AddTransient<TContract, TImpl>(bool allowOverride = false) where TImpl : class, TContract;

    /// <summary>
    /// 注册瞬时服务，使用运行时 <see cref="Type"/> 指定契约与实现类型。
    /// </summary>
    /// <param name="contract">服务契约类型。</param>
    /// <param name="impl">服务的具体实现类型，必须可赋值给 <paramref name="contract"/>。</param>
    /// <param name="allowOverride">是否允许顶掉该契约上先前的注册，默认 <c>false</c>（重复注册抛异常）。</param>
    /// <remarks>
    /// 用于无法使用泛型的反射或动态注册场景。
    /// </remarks>
    void AddTransient(Type contract, Type impl, bool allowOverride = false);

    /// <summary>
    /// 注册瞬时服务，使用工厂委托创建实例。
    /// </summary>
    /// <typeparam name="TContract">服务契约类型。</typeparam>
    /// <param name="factory">工厂委托，接收当前解析上下文容器作为参数并返回服务实例。</param>
    /// <param name="allowOverride">是否允许顶掉该契约上先前的注册，默认 <c>false</c>（重复注册抛异常）。</param>
    /// <remarks>
    /// 每次解析都会调用工厂创建一个新实例。
    /// </remarks>
    void AddTransient<TContract>(Func<IVivContainer, TContract> factory, bool allowOverride = false) where TContract : class;

    /// <summary>
    /// 注册瞬时服务，契约就是实现类型自身。
    /// </summary>
    /// <typeparam name="TImpl">服务的具体实现类型，同时用作契约。</typeparam>
    /// <remarks>
    /// 省掉把同一个类型写两遍。约束只有 <c>class</c>，没有双参版那条可赋值性要求 ——
    /// 契约本来就等于实现自己。
    ///
    /// 这一组刻意没有 <c>allowOverride</c>：契约就是实现自己，覆盖等于用同一个类型顶掉
    /// 同一个契约，没有语义可表。要换实现必然换契约，那就走双参版那条路。
    /// </remarks>
    void AddTransient<TImpl>() where TImpl : class;

    /// <summary>
    /// 注册瞬时服务，契约就是实现类型自身，使用运行时 <see cref="Type"/>。
    /// </summary>
    /// <param name="impl">服务的具体实现类型，同时用作契约。</param>
    /// <remarks>
    /// 用于无法使用泛型的反射或动态注册场景。
    /// </remarks>
    void AddTransient(Type impl);

    /// <summary>
    /// 解析指定契约类型的服务实例。
    /// </summary>
    /// <typeparam name="TContract">要解析的服务契约类型。</typeparam>
    /// <returns>解析得到的服务实例。</returns>
    /// <exception cref="InvalidOperationException">
    /// 当 <typeparamref name="TContract"/> 未注册或无法解析时抛出。
    /// </exception>
    /// <remarks>
    /// 这是必须成功的解析方式，未注册时应当抛出异常而非返回 <c>null</c>。
    /// </remarks>
    TContract GetService<TContract>();

    /// <summary>
    /// 解析指定运行时类型的服务实例。
    /// </summary>
    /// <param name="serviceType">要解析的服务类型。</param>
    /// <returns>解析得到的服务实例；若未注册则返回 <c>null</c>。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="serviceType"/> 为 <c>null</c> 时抛出。</exception>
    /// <remarks>
    /// 用于反射或框架集成场景，行为与 <see cref="TryGetService{TContract}"/> 一致但不会抛未注册异常。
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
    /// <returns>解析成功返回 <c>true</c>，未注册时返回 <c>false</c>。</returns>
    /// <remarks>
    /// 只有「没注册」这一种情况返回 <c>false</c>。注册了但构造不出来（依赖缺失、
    /// 没有公共构造函数、循环依赖）仍然抛异常 —— 那些是真缺陷，吞掉只会变成更难查的
    /// 空引用。适合可选的依赖场景；若依赖为必需，请使用 <see cref="GetService{TContract}"/>。
    /// </remarks>
    bool TryGetService<TContract>([NotNullWhen(true)] out TContract? service);

    /// <summary>
    /// 创建一个新的作用域容器，用于解析 Scoped 服务。
    /// </summary>
    /// <returns>新建的作用域实例。</returns>
    /// <remarks>
    /// 调用方负责释放该作用域，通常配合 <c>using</c> 或 <c>await using</c> 使用。
    /// 每个作用域内 Scoped 服务独立缓存。
    /// </remarks>
    IVivScope CreateScope();
}