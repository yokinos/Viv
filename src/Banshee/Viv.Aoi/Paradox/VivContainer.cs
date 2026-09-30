using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Viv.Aoi.Paradox
{
    /// <summary>
    /// Viv 默认的轻量级 IOC 容器实现。
    /// </summary>
    /// <remarks>
    /// 支持 Singleton / Scoped / Transient 三种生命周期，构造函数注入、工厂注册、
    /// 循环依赖检测，以及 IDisposable / IAsyncDisposable 自动释放。
    ///
    /// 线程安全：注册与解析都走内部锁。描述符表与实例缓存是一把锁，实例构造另有一把，
    /// 所以构造函数慢不会把别的解析一起拖住。
    ///
    /// 构造时可以传一个 fallback，自己没注册的契约转交过去，「大部分服务归 Viv、
    /// 少数几个还留在宿主」的过渡期不必一次搬完。桥接是单向的：容器能当 MS DI 的
    /// <see cref="IServiceProvider"/> 用，反过来不行；要用 <see cref="IServiceScopeFactory"/>
    /// 得套一层 <see cref="ServiceProviderVivContainer"/>。
    /// </remarks>
    public sealed class VivContainer : IVivContainer, IVivScope
    {
        private readonly Lock _sync = new();

        /// <summary>
        /// 只用来串行化本容器的实例构造，与 <see cref="_sync"/> 分开。
        /// </summary>
        /// <remarks>
        /// 合成一把的话，一个构造函数慢的单例会把所有解析一起按住 —— 连完全无关的
        /// Transient 和已经建好的实例也跑不掉，因为那些同样要读描述符表。
        ///
        /// 每个容器各有一把：单例用根容器那把，Scoped 用所属作用域那把，所以并发的
        /// 不同请求之间不打架。之所以不做成每类型一把，是因为 A、B 两个单例互相依赖时
        /// 两条线程会各持一把互等而挂死 —— 那种写法现在的结果是干净地抛循环依赖，
        /// 变成「单线程测得出、并发下才挂」最难查。
        /// </remarks>
        private readonly Lock _constructionSync = new();

        /// <summary>所有服务描述符。根容器与作用域共享同一份，读写统一走根容器的锁。</summary>
        private readonly Dictionary<Type, ServiceDescriptor> _descriptors;

        /// <summary>Singleton 实例缓存（所有作用域共享）。</summary>
        private readonly Dictionary<Type, object> _singletons;

        /// <summary>Scoped 实例缓存（每个作用域独立）。</summary>
        private readonly Dictionary<Type, object> _scopedInstances;

        /// <summary>需要释放的实例列表（Singleton 在根容器，Scoped 在各自作用域）。</summary>
        private readonly List<object> _disposables;

        /// <summary>根容器引用。若自身是根容器则指向 this。</summary>
        private readonly VivContainer _root;

        /// <summary>是否为作用域实例。</summary>
        private readonly bool _isScope;

        /// <summary>本容器没注册的服务转交给它。构造时定下，之后不再变。</summary>
        private readonly IServiceProvider? _fallback;

        /// <summary>fallback 上与本容器同生命周期的那层作用域，释放时一并释放。</summary>
        private readonly IServiceScope? _fallbackScope;

        /// <summary>
        /// 从 fallback 上取一次的 IServiceScopeFactory，给 CreateScope 开对应的 fallback 作用域用。
        /// 只从根容器取一次：从作用域里取到的那个与根上是同一个实例，再取没意义。
        /// 取不到就说明传进来的 provider 不开子作用域，那就全程共用它本身。
        /// </summary>
        private readonly IServiceScopeFactory? _fallbackFactory;

        /// <summary>每类型缓存的构造函数，省掉每次解析都跑一遍反射。</summary>
        private static readonly ConcurrentDictionary<Type, ConstructorInfo> _constructorCache = new();

        /// <summary>
        /// 正在构造中的服务类型，按进入顺序排列（线程本地）。
        ///
        /// 用 List 而不是 HashSet 是因为链要按顺序打出来给人看，而 HashSet 的枚举顺序不保证。
        /// 链本身很短，Contains 的线性查找可以忽略。
        /// </summary>
        [ThreadStatic]
        private static List<Type>? _resolutionStack;

        /// <summary>
        /// 当前线程上「单例构造」的嵌套层数。
        ///
        /// 这个标记不能靠参数往下传：工厂注册的形态是 c =&gt; new T(c.GetService&lt;IScoped&gt;())，
        /// 工厂拿到的是根容器、回调的是公开的 GetService，参数传下来的标记会在那里断掉。
        /// 线程本地量才跟得住。
        /// </summary>
        [ThreadStatic]
        private static int _singletonDepth;

        private volatile bool _disposed;

        /// <summary>
        /// 创建一个根容器。
        /// </summary>
        /// <param name="fallback">
        /// 本容器没有的服务转交给它，通常传宿主自己的 <see cref="IServiceProvider"/>。为 <c>null</c> 表示不桥接。
        /// </param>
        /// <remarks>
        /// fallback 由调用方持有，容器释放时不会释放它，只释放从它上面开的那些子作用域。
        /// </remarks>
        public VivContainer(IServiceProvider? fallback = null)
        {
            _descriptors = new Dictionary<Type, ServiceDescriptor>();
            _singletons = new Dictionary<Type, object>();
            _scopedInstances = new Dictionary<Type, object>();
            _disposables = new List<object>();
            _root = this;
            _isScope = false;
            _fallback = fallback;
            _fallbackFactory = fallback?.GetService<IServiceScopeFactory>();
        }

        /// <summary>
        /// 由根容器创建一个作用域。
        /// </summary>
        /// <param name="fallbackScope">fallback 上对应的一层作用域，没有桥接时为 <c>null</c>。</param>
        private VivContainer(VivContainer root, IServiceScope? fallbackScope)
        {
            _descriptors = root._descriptors;
            _singletons = root._singletons;
            _scopedInstances = new Dictionary<Type, object>();
            _disposables = new List<object>();
            _root = root;
            _isScope = true;
            _fallbackScope = fallbackScope;
            _fallback = fallbackScope?.ServiceProvider ?? root._fallback;
            _fallbackFactory = root._fallbackFactory;
        }

        /// <summary>
        /// 本容器没注册的服务都会转交给它，建容器时定下。
        /// </summary>
        /// <remarks>
        /// 作用域上读到的是它自己那份 fallback 作用域的 provider，不是建根容器时传进来的那一个。
        /// </remarks>
        public IServiceProvider? Fallback => _fallback;

        /// <inheritdoc />
        public void AddSingleton<TContract, TImpl>(bool allowOverride = false) where TImpl : class, TContract
            => AddSingleton(typeof(TContract), typeof(TImpl), allowOverride);

        /// <inheritdoc />
        public void AddSingleton(Type contract, Type impl, bool allowOverride = false)
        {
            ArgumentNullException.ThrowIfNull(contract);
            ArgumentNullException.ThrowIfNull(impl);
            AddDescriptor(new ServiceDescriptor(contract, impl, ServiceLifetime.Singleton), allowOverride);
        }

        /// <inheritdoc />
        public void AddSingleton<TContract>(TContract instance, bool allowOverride = false) where TContract : class
        {
            ArgumentNullException.ThrowIfNull(instance);
            AddDescriptor(new ServiceDescriptor(typeof(TContract), instance, ServiceLifetime.Singleton), allowOverride);
        }

        /// <inheritdoc />
        public void AddSingleton<TContract>(Func<IVivContainer, TContract> factory, bool allowOverride = false) where TContract : class
        {
            ArgumentNullException.ThrowIfNull(factory);
            AddDescriptor(new ServiceDescriptor(typeof(TContract), c => factory(c)!, ServiceLifetime.Singleton), allowOverride);
        }

        /// <inheritdoc />
        public void AddSingleton<TImpl>() where TImpl : class
            => AddSingleton(typeof(TImpl), typeof(TImpl));

        /// <inheritdoc />
        public void AddSingleton(Type impl) => AddSingleton(impl, impl);


        /// <inheritdoc />
        public void AddScoped<TContract, TImpl>(bool allowOverride = false) where TImpl : class, TContract
            => AddScoped(typeof(TContract), typeof(TImpl), allowOverride);

        /// <inheritdoc />
        public void AddScoped(Type contract, Type impl, bool allowOverride = false)
        {
            ArgumentNullException.ThrowIfNull(contract);
            ArgumentNullException.ThrowIfNull(impl);
            AddDescriptor(new ServiceDescriptor(contract, impl, ServiceLifetime.Scoped), allowOverride);
        }

        /// <inheritdoc />
        public void AddScoped<TContract>(Func<IVivContainer, TContract> factory, bool allowOverride = false) where TContract : class
        {
            ArgumentNullException.ThrowIfNull(factory);
            AddDescriptor(new ServiceDescriptor(typeof(TContract), c => factory(c)!, ServiceLifetime.Scoped), allowOverride);
        }

        /// <inheritdoc />
        public void AddScoped<TImpl>() where TImpl : class
            => AddScoped<TImpl, TImpl>();

        /// <inheritdoc />
        public void AddScoped(Type impl) => AddScoped(impl, impl);


        /// <inheritdoc />
        public void AddTransient<TContract, TImpl>(bool allowOverride = false) where TImpl : class, TContract
            => AddTransient(typeof(TContract), typeof(TImpl), allowOverride);

        /// <inheritdoc />
        public void AddTransient(Type contract, Type impl, bool allowOverride = false)
        {
            ArgumentNullException.ThrowIfNull(contract);
            ArgumentNullException.ThrowIfNull(impl);
            AddDescriptor(new ServiceDescriptor(contract, impl, ServiceLifetime.Transient), allowOverride);
        }

        /// <inheritdoc />
        public void AddTransient<TContract>(Func<IVivContainer, TContract> factory, bool allowOverride = false) where TContract : class
        {
            ArgumentNullException.ThrowIfNull(factory);
            AddDescriptor(new ServiceDescriptor(typeof(TContract), c => factory(c)!, ServiceLifetime.Transient), allowOverride);
        }

        /// <inheritdoc />
        public void AddTransient<TImpl>() where TImpl : class
            => AddTransient(typeof(TImpl), typeof(TImpl));

        /// <inheritdoc />
        public void AddTransient(Type impl) => AddTransient(impl, impl);

        /// <summary>
        /// 把一个描述符登记进表。同一个契约只留一个实现，重复注册默认抛异常。
        /// </summary>
        /// <param name="descriptor">要登记的描述符。</param>
        /// <param name="allowOverride">是否允许后来的顶掉先前的。</param>
        private void AddDescriptor(ServiceDescriptor descriptor, bool allowOverride)
        {
            ThrowIfDisposed();

            if (_isScope)
                throw new NotSupportedException("不能在作用域中注册服务，请在根容器上注册。");

            if (descriptor.ImplementationType is not null
                && !descriptor.ServiceType.IsAssignableFrom(descriptor.ImplementationType))
            {
                throw new ArgumentException(
                    $"类型 {descriptor.ImplementationType.FullName} 未实现 {descriptor.ServiceType.FullName}。");
            }

            lock (_root._sync)
            {
                _descriptors.TryGetValue(descriptor.ServiceType, out var existing);

                if (existing is not null)
                {
                    if (!allowOverride)
                    {
                        throw new InvalidOperationException(
                            $"服务 {descriptor.ServiceType.FullName} 已注册。同一个契约只能有一个实现，" +
                            "要换实现请换一个契约类型、把容器建成新的，或者注册时传 allowOverride: true。");
                    }

                    // 已经建出过实例的契约覆盖不动 —— 解析走的是单例缓存里的那份，只换描述符
                    // 等于新注册永远不生效，而编译和测试都不会吭声。判据不能只看缓存里有没有：
                    // 实例注册从登记那刻就压在那儿，它本来就现成，顶掉没有构造过程可言。
                    if (existing.Instance is null && _singletons.ContainsKey(descriptor.ServiceType))
                    {
                        throw new InvalidOperationException(
                            $"服务 {descriptor.ServiceType.FullName} 已经解析过了，不能再覆盖。" +
                            "覆盖只应该在装配期做 —— 一旦有人解析过，注册就该冻结。");
                    }
                }

                if (descriptor.Instance is not null)
                {
                    _singletons[descriptor.ServiceType] = descriptor.Instance;
                    TrackDisposable(descriptor.Instance);
                }
                else if (existing?.Instance is not null)
                {
                    // 被顶掉的那份是实例注册，它还压在单例缓存里 —— 不摘掉的话解析先撞上它，
                    // 新的描述符根本没机会被看到。旧实例仍留在释放清单里：登记那刻所有权就
                    // 转移给容器了，半路摘掉等于让它没人管。
                    _singletons.Remove(descriptor.ServiceType);
                }

                _descriptors[descriptor.ServiceType] = descriptor;
            }
        }


        /// <inheritdoc />
        public TContract GetService<TContract>()
        {
            var service = GetService(typeof(TContract));
            if (service is null)
                throw new InvalidOperationException($"未注册服务：{typeof(TContract).FullName}。");
            return (TContract)service;
        }

        /// <inheritdoc />
        public object? GetService(Type serviceType)
        {
            ArgumentNullException.ThrowIfNull(serviceType);
            ThrowIfDisposed();
            return Resolve(serviceType, this);
        }

        /// <inheritdoc />
        public bool TryGetService<TContract>([NotNullWhen(true)] out TContract? service)
        {
            var obj = GetService(typeof(TContract));
            if (obj is null)
            {
                service = default;
                return false;
            }

            service = (TContract)obj;
            return true;
        }


        /// <inheritdoc />
        public IVivScope CreateScope()
        {
            ThrowIfDisposed();

            // fallback 那边也要跟着开一层，否则从 fallback 解析出来的 Scoped 会落在它的根上，
            // 每个 Viv 作用域拿到的都是同一个实例，Scoped 静默变单例。
            return new VivContainer(_root, _fallbackFactory?.CreateScope());
        }

        /// <inheritdoc />
        public bool IsService(Type serviceType)
        {
            ArgumentNullException.ThrowIfNull(serviceType);

            // 这里只读不建，所以不查释放状态：释放之后再问一声不该炸，
            // 真去解析时 GetService 自然会抛 ObjectDisposedException。

            if (ResolveSelf(serviceType) is not null)
                return true;

            lock (_root._sync)
            {
                if (_descriptors.ContainsKey(serviceType))
                    return true;
            }

            if (_fallback is null)
                return false;

            // MS DI 的具体 ServiceProvider 自己并不实现 IServiceProviderIsService —— 实现它的是内部的
            // CallSiteFactory，只能从 provider 里解析出来（ISupportRequiredService 也是这个形状，
            // 直接 is 一下恒为 false）。所以先按实例问，问不到再解析一次。
            if (_fallback is IServiceProviderIsService direct)
                return direct.IsService(serviceType);

            return _fallback.GetService<IServiceProviderIsService>()?.IsService(serviceType) ?? false;
        }

        /// <inheritdoc />
        public object GetRequiredService(Type serviceType)
        {
            ArgumentNullException.ThrowIfNull(serviceType);

            return GetService(serviceType)
                ?? throw new InvalidOperationException($"未注册服务：{serviceType.FullName}。");
        }

        /// <summary>
        /// 容器自身的契约，不走注册表，直接给实例。作用域给作用域自己，
        /// <see cref="IVivContainer"/> 一律给根容器 —— 注册动作本来就只在根上做。
        /// </summary>
        private object? ResolveSelf(Type serviceType)
        {
            if (serviceType == typeof(IServiceProvider)
                || serviceType == typeof(IServiceProviderIsService)
                || serviceType == typeof(ISupportRequiredService)
                || serviceType == typeof(IServiceScope)
                || serviceType == typeof(IVivScope))
            {
                return this;
            }

            if (serviceType == typeof(IVivContainer))
                return _root;

            return null;
        }

        /// <inheritdoc />
        IServiceProvider IServiceScope.ServiceProvider => this;


        private object? Resolve(Type serviceType, VivContainer scope)
        {
            var self = scope.ResolveSelf(serviceType);
            if (self is not null)
                return self;

            ServiceDescriptor? descriptor;
            lock (_root._sync)
            {
                _descriptors.TryGetValue(serviceType, out descriptor);
            }

            // 转交给 scope 自己那份，不是根那份 —— 转给根的话，从 fallback 解析出来的
            // Scoped 全挂在它的根上，每个 Viv 作用域拿到的都是同一个实例，Scoped 静默变单例。
            if (descriptor is null)
                return scope._fallback?.GetService(serviceType);

            if (descriptor.Lifetime == ServiceLifetime.Scoped && _singletonDepth > 0)
            {
                var owner = _resolutionStack is { Count: > 0 } ? _resolutionStack[0].Name : "某个单例";
                throw new InvalidOperationException(
                    $"单例 {owner} 依赖了作用域服务 {serviceType.FullName}。" +
                    "单例活在根容器上，这么写会把这个 Scoped 一并提升成事实上的单例，跨作用域共用一份。");
            }

            switch (descriptor.Lifetime)
            {
                case ServiceLifetime.Singleton:
                    return ResolveSingleton(descriptor);

                case ServiceLifetime.Scoped:
                    return ResolveScoped(descriptor, scope);

                case ServiceLifetime.Transient:
                    return CreateInstance(descriptor, scope);

                default:
                    throw new InvalidOperationException($"未知的生命周期：{descriptor.Lifetime}");
            }
        }

        private object ResolveSingleton(ServiceDescriptor descriptor)
        {
            var key = descriptor.ServiceType;

            // 快路径：建好的单例只碰数据锁，不进构造锁 —— 不然每个已建实例的解析都要
            // 排在别人的慢构造函数后面。
            lock (_root._sync)
            {
                if (_root._singletons.TryGetValue(key, out var ready))
                    return ready;
            }

            lock (_root._constructionSync)
            {
                // 双检：等在构造锁外面的这段时间里，别人可能已经建好了。
                lock (_root._sync)
                {
                    if (_root._singletons.TryGetValue(key, out var raced))
                        return raced;
                }

                _singletonDepth++;
                object instance;
                try
                {
                    instance = CreateInstance(descriptor, _root);
                }
                finally
                {
                    _singletonDepth--;
                }

                lock (_root._sync)
                {
                    _root._singletons[key] = instance;
                    _root.TrackDisposable(instance);
                }

                return instance;
            }
        }

        private object ResolveScoped(ServiceDescriptor descriptor, VivContainer scope)
        {
            var key = descriptor.ServiceType;

            lock (scope._sync)
            {
                if (scope._scopedInstances.TryGetValue(key, out var ready))
                    return ready;
            }

            // 构造不能压着 scope._sync 走：作用域解析出未建的单例时，要在持数据锁的情况下
            // 去拿构造锁，而单例那条路径的顺序正好相反，两边一撞就是死锁。构造锁是各自
            // 作用域一把，所以不同请求之间不受影响。
            lock (scope._constructionSync)
            {
                lock (scope._sync)
                {
                    if (scope._scopedInstances.TryGetValue(key, out var raced))
                        return raced;
                }

                var instance = CreateInstance(descriptor, scope);

                lock (scope._sync)
                {
                    scope._scopedInstances[key] = instance;
                    scope.TrackDisposable(instance);
                }

                return instance;
            }
        }

        /// <summary>
        /// 建实例。工厂与构造函数注入共用这里的循环检测 —— 只要漏掉工厂那条路，
        /// 两个工厂互相解析就是一个不可捕获的栈溢出，进程当场没了。
        /// </summary>
        private object CreateInstance(ServiceDescriptor descriptor, VivContainer scope)
        {
            var key = descriptor.ServiceType;

            _resolutionStack ??= new List<Type>();
            if (_resolutionStack.Contains(key))
            {
                throw new InvalidOperationException(
                    $"检测到循环依赖：{string.Join(" -> ", _resolutionStack)} -> {key.Name}");
            }

            _resolutionStack.Add(key);
            try
            {
                if (descriptor.Factory is not null)
                    return descriptor.Factory(scope);

                return CreateWithConstructorInjection(descriptor.ImplementationType!, scope);
            }
            finally
            {
                _resolutionStack.Remove(key);
            }
        }

        private object CreateWithConstructorInjection(Type implType, VivContainer scope)
        {
            var ctor = SelectConstructor(implType);
            var parameters = ctor.GetParameters();
            var args = new object?[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var paramType = parameters[i].ParameterType;
                var dep = Resolve(paramType, scope);

                if (dep is null)
                {
                    // 参数有默认值则使用默认值
                    if (parameters[i].HasDefaultValue)
                    {
                        args[i] = parameters[i].DefaultValue;
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"无法解析 {implType.FullName} 的构造函数参数 {paramType.FullName}。");
                }

                args[i] = dep;
            }

            return ctor.Invoke(args);
        }

        private static ConstructorInfo SelectConstructor(Type implType)
        {
            return _constructorCache.GetOrAdd(implType, static type =>
            {
                var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
                if (ctors.Length == 0)
                    throw new InvalidOperationException($"类型 {type.FullName} 没有可用的公共构造函数。");

                // 选参数最多的；个数打平时按元数据顺序定，保证同一类型每次选到的是同一个。
                return ctors
                    .OrderByDescending(c => c.GetParameters().Length)
                    .ThenBy(c => c.MetadataToken)
                    .First();
            });
        }

        private void TrackDisposable(object instance)
        {
            if (instance is IDisposable || instance is IAsyncDisposable)
                _disposables.Add(instance);
        }

        /// <summary>
        /// 取走待释放实例并标记已释放。已释放过则返回 null，让 Dispose / DisposeAsync 各自早退。
        /// </summary>
        private List<object>? CollectDisposables()
        {
            // 与构造互斥，且顺序与解析那边一致（先构造锁再数据锁）。构造期间数据锁是反复
            // 拿放的，不挡一下的话这里能把实例缓存清空、置上 _disposed，随后构造完的实例
            // 又发布进去 —— 那个实例就永远不会被释放了。
            lock (_constructionSync)
            {
                lock (_sync)
                {
                    if (_disposed)
                        return null;

                    _disposed = true;

                    var toDispose = new List<object>(_disposables);
                    _disposables.Clear();
                    if (!_isScope)
                    {
                        _singletons.Clear();
                    }
                    _scopedInstances.Clear();
                    return toDispose;
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            var toDispose = CollectDisposables();
            if (toDispose is null)
                return;

            // 逆序释放。释放异常一律吞掉 —— 容器没法把 Dispose 变成会抛的方法，
            // 一条实例释放失败也不该拦住后面那些。
            for (int i = toDispose.Count - 1; i >= 0; i--)
            {
                switch (toDispose[i])
                {
                    case IDisposable d:
                        try { d.Dispose(); }
                        catch { }
                        break;

                    case IAsyncDisposable ad:
                        // 同步释放路径：阻塞等待
                        try { ad.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                        catch { }
                        break;
                }
            }

            // 自己的放完再放 fallback 那层，与构造顺序相反，和实例的逆序释放同理。
            _fallbackScope?.Dispose();
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            var toDispose = CollectDisposables();
            if (toDispose is null)
                return;

            // 与同步路径同一套规则，只是顺序反过来：异步路径优先走 DisposeAsync。
            for (int i = toDispose.Count - 1; i >= 0; i--)
            {
                switch (toDispose[i])
                {
                    case IAsyncDisposable ad:
                        try { await ad.DisposeAsync().ConfigureAwait(false); }
                        catch { }
                        break;

                    case IDisposable d:
                        try { d.Dispose(); }
                        catch { }
                        break;
                }
            }

            if (_fallbackScope is IAsyncDisposable fallbackAsync)
            {
                try { await fallbackAsync.DisposeAsync().ConfigureAwait(false); }
                catch { }
            }
            else
            {
                try { _fallbackScope?.Dispose(); }
                catch { }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(VivContainer));
        }

        private enum ServiceLifetime
        {
            Singleton,
            Scoped,
            Transient,
        }

        private sealed class ServiceDescriptor
        {
            public Type ServiceType { get; }
            public Type? ImplementationType { get; }
            public object? Instance { get; }
            public Func<VivContainer, object>? Factory { get; }
            public ServiceLifetime Lifetime { get; }

            // 构造函数注入
            public ServiceDescriptor(Type serviceType, Type implementationType, ServiceLifetime lifetime)
            {
                ServiceType = serviceType;
                ImplementationType = implementationType;
                Lifetime = lifetime;
            }

            // 实例注册
            public ServiceDescriptor(Type serviceType, object instance, ServiceLifetime lifetime)
            {
                ServiceType = serviceType;
                Instance = instance;
                Lifetime = lifetime;
            }

            // 工厂注册
            public ServiceDescriptor(Type serviceType, Func<VivContainer, object> factory, ServiceLifetime lifetime)
            {
                ServiceType = serviceType;
                Factory = factory;
                Lifetime = lifetime;
            }
        }
    }
}
