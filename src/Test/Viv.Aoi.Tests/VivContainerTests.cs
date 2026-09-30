using Microsoft.Extensions.DependencyInjection;
using Viv.Aoi.Paradox;

namespace Viv.Aoi.Tests
{
    public class VivContainerTests
    {
        [Fact]
        public void 实例注册能解析出原实例()
        {
            using var container = new VivContainer();
            var instance = new Foo();

            container.AddSingleton<IFoo>(instance);

            Assert.Same(instance, container.GetService<IFoo>());
        }

        [Fact]
        public void 工厂造环抛异常而不是栈溢出()
        {
            using var container = new VivContainer();
            container.AddSingleton<IA>(c => new A(c.GetService<IB>()));
            container.AddSingleton<IB>(c => new B(c.GetService<IA>()));

            var ex = Assert.Throws<InvalidOperationException>(() => container.GetService<IA>());
            Assert.Contains("循环依赖", ex.Message);
        }

        [Fact]
        public void 构造注入造环抛异常()
        {
            using var container = new VivContainer();
            container.AddSingleton<IA, A>();
            container.AddSingleton<IB, B>();

            var ex = Assert.Throws<InvalidOperationException>(() => container.GetService<IA>());
            Assert.Contains("循环依赖", ex.Message);
        }

        [Fact]
        public void 单例依赖作用域服务时抛异常()
        {
            using var container = new VivContainer();
            container.AddScoped<IScoped, ScopedThing>();
            container.AddSingleton<SingletonNeedsScoped, SingletonNeedsScoped>();

            var ex = Assert.Throws<InvalidOperationException>(() => container.GetService<SingletonNeedsScoped>());
            Assert.Contains("作用域服务", ex.Message);
        }

        [Fact]
        public void 工厂注册的单例依赖作用域服务时也抛异常()
        {
            using var container = new VivContainer();
            container.AddScoped<IScoped, ScopedThing>();
            container.AddSingleton<ISingletonNeedsScoped>(c => new SingletonNeedsScoped(c.GetService<IScoped>()));

            var ex = Assert.Throws<InvalidOperationException>(() => container.GetService<ISingletonNeedsScoped>());
            Assert.Contains("作用域服务", ex.Message);
        }

        [Fact]
        public void 作用域服务依赖单例是允许的()
        {
            using var container = new VivContainer();
            container.AddSingleton<IFoo, Foo>();
            container.AddScoped<IScopedNeedsSingleton, ScopedNeedsSingleton>();

            using var scope = container.CreateScope();
            var got = (ScopedNeedsSingleton)scope.GetService<IScopedNeedsSingleton>();

            Assert.NotNull(got.Foo);
        }

        [Fact]
        public void 同一契约重复注册抛异常()
        {
            using var container = new VivContainer();
            container.AddScoped<IBar, Bar>();

            Assert.Throws<InvalidOperationException>(() => container.AddScoped<IBar, Bar2>());
        }

        [Fact]
        public void 注册未实现契约的类型抛异常()
        {
            using var container = new VivContainer();

            // 泛型重载的 where TImpl : TContract 在编译期就拦住了，可赋值性校验只在 Type 这条路上有用。
            Assert.Throws<ArgumentException>(() => container.AddScoped(typeof(IBar), typeof(Unrelated)));
        }

        [Fact]
        public void 自注册的单例契约就是实现类自己()
        {
            using var container = new VivContainer();
            container.AddSingleton<SelfRegistered>();

            Assert.Same(container.GetService<SelfRegistered>(), container.GetService<SelfRegistered>());
        }

        [Fact]
        public void 自注册的作用域服务每个作用域一份()
        {
            using var container = new VivContainer();
            container.AddScoped<SelfRegistered>();

            using var scope1 = container.CreateScope();
            using var scope2 = container.CreateScope();

            Assert.Same(scope1.GetService<SelfRegistered>(), scope1.GetService<SelfRegistered>());
            Assert.NotSame(scope1.GetService<SelfRegistered>(), scope2.GetService<SelfRegistered>());
        }

        [Fact]
        public void 自注册的瞬时服务每次都是新实例()
        {
            using var container = new VivContainer();
            container.AddTransient<SelfRegistered>();

            Assert.NotSame(container.GetService<SelfRegistered>(), container.GetService<SelfRegistered>());
        }

        [Fact]
        public void 自注册的Type版三种生命周期都能用()
        {
            using var container = new VivContainer();
            container.AddSingleton(typeof(SelfRegistered));
            container.AddScoped(typeof(SelfScoped));
            container.AddTransient(typeof(SelfTransient));

            using var scope = container.CreateScope();

            Assert.Same(container.GetService<SelfRegistered>(), scope.GetService<SelfRegistered>());
            Assert.Same(scope.GetService<SelfScoped>(), scope.GetService<SelfScoped>());
            Assert.NotSame(scope.GetService<SelfTransient>(), scope.GetService<SelfTransient>());
        }

        [Fact]
        public void 自注册与双参注册占用同一个契约()
        {
            using var container = new VivContainer();
            container.AddScoped<SelfRegistered>();

            // 契约就是自己，所以查重跟双参那条走的是同一份判断。
            Assert.Throws<InvalidOperationException>(() => container.AddTransient<SelfRegistered>());
        }

        [Fact]
        public void 加自注册重载没有改变实例与工厂重载的绑定()
        {
            using var container = new VivContainer();
            var instance = new SelfRegistered();

            container.AddSingleton<SelfRegistered>(instance);
            container.AddScoped<SelfFactoryMade>(_ => new SelfFactoryMade());

            Assert.Same(instance, container.GetService<SelfRegistered>());
            using var scope = container.CreateScope();
            Assert.Same(scope.GetService<SelfFactoryMade>(), scope.GetService<SelfFactoryMade>());
        }

        [Fact]
        public void 允许覆盖时后来的顶掉先前的()
        {
            using var container = new VivContainer();
            container.AddSingleton<IFoo, Foo>();
            container.AddSingleton<IFoo, Foo2>(allowOverride: true);

            Assert.IsType<Foo2>(container.GetService<IFoo>());
        }

        [Fact]
        public void 覆盖对作用域与瞬时同样生效()
        {
            using var container = new VivContainer();
            container.AddScoped<IScoped, ScopedThing>();
            container.AddScoped<IScoped, ScopedThing2>(allowOverride: true);
            container.AddTransient<IBar, Bar>();
            container.AddTransient<IBar, Bar2>(allowOverride: true);

            using var scope = container.CreateScope();

            Assert.IsType<ScopedThing2>(scope.GetService<IScoped>());
            Assert.IsType<Bar2>(container.GetService<IBar>());
        }

        [Fact]
        public void 实例注册可以被覆盖()
        {
            using var container = new VivContainer();
            var original = new Foo();
            var replacement = new Foo2();

            container.AddSingleton<IFoo>(original);
            container.AddSingleton<IFoo>(replacement, allowOverride: true);

            // 实例注册从登记那刻就压在单例缓存里，光换描述符是覆盖不掉的。
            Assert.Same(replacement, container.GetService<IFoo>());
        }

        [Fact]
        public void 已解析过的单例不能再被覆盖()
        {
            using var container = new VivContainer();
            container.AddSingleton<IFoo, Foo>();
            container.GetService<IFoo>();

            var ex = Assert.Throws<InvalidOperationException>(
                () => container.AddSingleton<IFoo, Foo2>(allowOverride: true));

            Assert.Contains("已经解析过", ex.Message);
        }

        [Fact]
        public void 瞬时服务解析过之后仍然可以被覆盖()
        {
            using var container = new VivContainer();
            container.AddTransient<IBar, Bar>();
            Assert.IsType<Bar>(container.GetService<IBar>());

            // 冻结判的是单例缓存，瞬时没有缓存，也就没有「覆盖了却没生效」这回事。
            container.AddTransient<IBar, Bar2>(allowOverride: true);

            Assert.IsType<Bar2>(container.GetService<IBar>());
        }

        [Fact]
        public void 被顶掉的实例注册仍然会被释放()
        {
            var container = new VivContainer();
            var original = new DisposableFoo();

            container.AddSingleton<IFoo>(original);
            container.AddSingleton<IFoo>(new Foo2(), allowOverride: true);

            container.Dispose();

            // 登记那刻所有权就转移给容器了，半路被顶掉不解除这件事。
            Assert.True(original.Disposed);
        }

        [Fact]
        public void 未注册的契约解析抛异常()
        {
            using var container = new VivContainer();

            Assert.Throws<InvalidOperationException>(() => container.GetService<IFoo>());
        }

        [Fact]
        public void 未注册的契约取运行时类型返回空()
        {
            using var container = new VivContainer();

            Assert.Null(container.GetService(typeof(IFoo)));
        }

        [Fact]
        public void TryGetService在未注册时返回false()
        {
            using var container = new VivContainer();

            Assert.False(container.TryGetService<IFoo>(out var service));
            Assert.Null(service);
        }

        [Fact]
        public void TryGetService在构造失败时仍然抛异常()
        {
            using var container = new VivContainer();
            container.AddTransient<IA, A>();

            Assert.Throws<InvalidOperationException>(() => container.TryGetService<IA>(out _));
        }

        [Fact]
        public void 作用域内同一个Scoped只创建一次()
        {
            using var container = new VivContainer();
            container.AddScoped<IScoped, ScopedThing>();

            using var scope = container.CreateScope();

            Assert.Same(scope.GetService<IScoped>(), scope.GetService<IScoped>());
        }

        [Fact]
        public void 不同作用域的Scoped互不共享()
        {
            using var container = new VivContainer();
            container.AddScoped<IScoped, ScopedThing>();

            using var scope1 = container.CreateScope();
            using var scope2 = container.CreateScope();

            Assert.NotSame(scope1.GetService<IScoped>(), scope2.GetService<IScoped>());
        }

        [Fact]
        public void 单例跨作用域共享()
        {
            using var container = new VivContainer();
            container.AddSingleton<IFoo, Foo>();

            using var scope1 = container.CreateScope();
            using var scope2 = container.CreateScope();

            Assert.Same(scope1.GetService<IFoo>(), scope2.GetService<IFoo>());
        }

        [Fact]
        public void 瞬时服务每次都是新实例()
        {
            using var container = new VivContainer();
            container.AddTransient<IFoo, Foo>();

            Assert.NotSame(container.GetService<IFoo>(), container.GetService<IFoo>());
        }

        [Fact]
        public void 在作用域上注册抛异常()
        {
            using var container = new VivContainer();
            using var scope = (VivContainer)container.CreateScope();

            Assert.Throws<NotSupportedException>(() => scope.AddSingleton<IFoo, Foo>());
        }

        [Fact]
        public void 构造函数参数有默认值时可缺省()
        {
            using var container = new VivContainer();
            container.AddTransient<IWithDefault, WithDefault>();

            var got = (WithDefault)container.GetService<IWithDefault>();

            Assert.Null(got.Dependency);
        }

        [Fact]
        public void 释放作用域会释放其中的Scoped()
        {
            var container = new VivContainer();
            container.AddScoped<IScoped, DisposableScoped>();

            DisposableScoped scoped;
            using (var scope = container.CreateScope())
            {
                scoped = (DisposableScoped)scope.GetService<IScoped>();
                Assert.False(scoped.Disposed);
            }

            Assert.True(scoped.Disposed);
            container.Dispose();
        }

        [Fact]
        public void 释放作用域不会释放单例()
        {
            var container = new VivContainer();
            container.AddSingleton<IFoo, DisposableFoo>();

            var singleton = (DisposableFoo)container.GetService<IFoo>();
            using (var scope = container.CreateScope())
            {
                scope.GetService<IFoo>();
            }

            Assert.False(singleton.Disposed);

            container.Dispose();
            Assert.True(singleton.Disposed);
        }

        [Fact]
        public void 释放后再解析抛异常()
        {
            var container = new VivContainer();
            container.AddSingleton<IFoo, Foo>();
            container.Dispose();

            Assert.Throws<ObjectDisposedException>(() => container.GetService<IFoo>());
        }

        [Fact]
        public async Task 单例构造期间解析别的服务不被挡住()
        {
            using var container = new VivContainer();
            var gate = new Gate();

            container.AddSingleton(gate);
            container.AddSingleton<IFoo, Foo>();
            container.AddTransient<IBar, Bar>();
            container.AddSingleton<GatedSingleton>();

            var ready = container.GetService<IFoo>();

            var slow = Task.Run(() => container.GetService<GatedSingleton>());
            Assert.True(gate.Entered.Wait(TimeSpan.FromSeconds(5)));

            // 慢构造函数正卡在锁里，这两条本该完全不受影响
            Assert.Same(ready, await ResolvesWithin(
                Task.Run(() => container.GetService<IFoo>()),
                "已建好的单例被别人的慢构造函数挡住了"));

            Assert.NotNull(await ResolvesWithin(
                Task.Run(() => container.GetService<IBar>()),
                "Transient 被别人的慢构造函数挡住了"));

            gate.Release.Set();
            await slow;
        }

        [Fact]
        public async Task 同一作用域内并发解析Scoped只建一份()
        {
            using var container = new VivContainer();
            var gate = new Gate();

            container.AddSingleton(gate);
            container.AddScoped<GatedScoped>();

            using var scope = container.CreateScope();

            var first = Task.Run(() => scope.GetService<GatedScoped>());
            Assert.True(gate.Entered.Wait(TimeSpan.FromSeconds(5)));

            var second = Task.Run(() => scope.GetService<GatedScoped>());

            gate.Release.Set();

            Assert.Same(await first, await second);
        }

        [Fact]
        public async Task 构造与释放撞上时实例仍被释放()
        {
            var container = new VivContainer();
            var gate = new Gate();

            container.AddSingleton(gate);
            container.AddSingleton<GatedDisposable>();

            var slow = Task.Run(() => container.GetService<GatedDisposable>());
            Assert.True(gate.Entered.Wait(TimeSpan.FromSeconds(5)));

            var dispose = Task.Run(container.Dispose);

            // 构造还卡着，释放得等它出来 —— 否则这里会把实例缓存清空、置上已释放，
            // 随后构造完的实例又发布进去，那个实例永远没人释放。
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            Assert.False(dispose.IsCompleted, "释放没有等构造结束");

            gate.Release.Set();

            var instance = await slow;
            await dispose;

            Assert.True(instance.Disposed);
        }

        [Fact]
        public void 用接口或抽象类当实现注册抛异常()
        {
            using var container = new VivContainer();

            // 泛型那条路编译期就拦住了，这两条是给 Type 那条路补的 —— 放过去的话
            // 要等到解析时才炸在「没有可用的公共构造函数」上，离出错的地方很远。
            Assert.Throws<ArgumentException>(() => container.AddScoped(typeof(IBar), typeof(IBar)));
            Assert.Throws<ArgumentException>(() => container.AddScoped(typeof(IBar), typeof(AbstractBar)));
            Assert.Throws<ArgumentException>(() => container.AddScoped(typeof(IBar)));
        }

        [Fact]
        public void 注册容器自身的契约抛异常()
        {
            using var container = new VivContainer();

            // 这些契约解析时直接给实例、压根不看描述符表，注册进去永远不会生效。
            Assert.Throws<ArgumentException>(() => container.AddSingleton(typeof(IServiceProvider), typeof(FakeProvider)));
            Assert.Throws<ArgumentException>(() => container.AddSingleton(typeof(IVivScope), typeof(VivContainer)));
            Assert.Throws<ArgumentException>(() => container.AddSingleton(typeof(IServiceScope), typeof(VivContainer)));
        }

        [Fact]
        public void 同一个实例注册在两个契约上只释放一次()
        {
            var container = new VivContainer();
            var shared = new DualContractThing();

            container.AddSingleton<IFoo>(shared);
            container.AddSingleton<IBar>(shared);

            container.Dispose();

            Assert.Equal(1, shared.DisposeCount);
        }

        [Fact]
        public void 取值相等但不同实例各自释放一次()
        {
            var container = new VivContainer();
            var first = new EquatableDisposable();
            var second = new EquatableDisposable();

            container.AddSingleton<IFoo>(first);
            container.AddSingleton<IBar>(second);

            // 按引用去重。拿 Equals 判重的话这两个对象会被并成一条，第二个就漏了 ——
            // 实例还在，只是没人释放它。
            Assert.Equal(first, second);

            container.Dispose();

            Assert.Equal(1, first.DisposeCount);
            Assert.Equal(1, second.DisposeCount);
        }

        [Fact]
        public void 两个释放接口都实现时同步释放也走异步()
        {
            var container = new VivContainer();
            var both = new BothDisposable();

            container.AddSingleton(both);
            container.Dispose();

            Assert.Equal(1, both.AsyncDisposeCount);
            Assert.Equal(0, both.SyncDisposeCount);
        }

        [Fact]
        public async Task 并发解析与释放不会交出没人释放的实例()
        {
            // 窄窗口压力测试：线程在 GetService 那次「已释放」检查和构造锁之间被换下去，
            // 释放正好在这中间整个跑完 —— 那时建出来的实例会发布进一份已经清空、
            // 再也不会有人来收的缓存。窗口只有几十纳秒，多跑几轮是唯一能碰到它的办法。
            for (int round = 0; round < 200; round++)
            {
                var container = new VivContainer();
                container.AddSingleton<DisposableSelf>();

                DisposableSelf? resolved = null;
                var resolve = Task.Run(() =>
                {
                    try { resolved = (DisposableSelf?)container.GetService(typeof(DisposableSelf)); }
                    catch (ObjectDisposedException) { }
                });

                var dispose = Task.Run(container.Dispose);

                await Task.WhenAll(resolve, dispose);

                // 要么解析被释放挡下来，要么交出去的实例必须已经释放 ——
                // 不能有第三种结果：实例交出去了，却再没人会释放它。
                if (resolved is not null)
                    Assert.True(resolved.Disposed, $"第 {round} 轮：交出去的实例没有被释放");
            }
        }

        [Fact]
        public void 根上问自身契约不会漏到fallback()
        {
            using var inner = new VivContainer();
            using var outer = new VivContainer(inner.CreateScope());

            // fallback 是另一层 Viv 作用域时它答得出这两个契约。根的 null 只是「当前语境
            // 不给」，不是「不是自身契约」—— 顺下去就会把别人的作用域交出来。
            Assert.Null(outer.GetService(typeof(IVivScope)));
            Assert.Null(outer.GetService(typeof(IServiceScope)));
            Assert.False(outer.IsService(typeof(IVivScope)));
            Assert.False(outer.IsService(typeof(IServiceScope)));
        }

        [Fact]
        public void 作用域上仍拿得到自己那层()
        {
            using var container = new VivContainer();
            using var scope = (VivContainer)container.CreateScope();

            Assert.Same(scope, scope.GetService(typeof(IVivScope)));
            Assert.Same(scope, scope.GetService(typeof(IServiceScope)));
        }

        [Fact]
        public void 默认允许从根解析Scoped()
        {
            using var container = new VivContainer();
            container.AddScoped<IScoped, ScopedThing>();

            Assert.NotNull(container.GetService<IScoped>());
        }

        [Fact]
        public void 开着作用域校验时从根解析Scoped抛异常()
        {
            using var container = new VivContainer(validateScopes: true);
            container.AddScoped<IScoped, ScopedThing>();

            var ex = Assert.Throws<InvalidOperationException>(() => container.GetService<IScoped>());
            Assert.Contains("作用域校验", ex.Message);

            using var scope = container.CreateScope();
            Assert.NotNull(scope.GetService<IScoped>());
        }

        [Fact]
        public void 开着作用域校验时从根解析的Transient依赖Scoped也抛()
        {
            using var container = new VivContainer(validateScopes: true);
            container.AddScoped<IScoped, ScopedThing>();
            container.AddTransient<NeedsScoped>();

            // 开关真正要挡的是这条：Transient 从根拿本身没问题，问题是它被谁接住。
            Assert.Throws<InvalidOperationException>(() => container.GetService<NeedsScoped>());
        }

        [Fact]
        public void 开着作用域校验时不挡作用域里的Transient()
        {
            using var container = new VivContainer(validateScopes: true);
            container.AddScoped<IScoped, ScopedThing>();
            container.AddTransient<NeedsScoped>();

            using var scope = container.CreateScope();

            Assert.NotNull(scope.GetService<NeedsScoped>());
        }

        /// <summary>
        /// 等一个解析任务在限定时间内完成。
        /// </summary>
        /// <remarks>
        /// 要验的是「它没有被挡住」，所以只能在超时后拿断言报出来 —— 直接 await 的话
        /// 写错了就是测试整个挂住，看不出是哪一条。
        /// </remarks>
        private static async Task<T> ResolvesWithin<T>(Task<T> task, string because)
        {
            var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(3)));
            Assert.True(ReferenceEquals(done, task), because);

            return await task;
        }
    }

    public interface IFoo { }

    public sealed class Foo : IFoo { }

    public sealed class Foo2 : IFoo { }

    public sealed class DisposableFoo : IFoo, IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    public interface IBar { }

    /// <summary>与任何契约都没有实现关系，用于验证注册期的可赋值性校验。</summary>
    public sealed class Unrelated { }

    public sealed class Bar : IBar { }

    public sealed class Bar2 : IBar { }

    /// <summary>抽象实现，用来验证注册期的实现类型校验。</summary>
    public abstract class AbstractBar : IBar { }

    /// <summary>只用来当 <see cref="IServiceProvider"/> 的实现，验注册期的自身契约校验。</summary>
    public sealed class FakeProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    /// <summary>两个契约共用一个实例，释放只该发生一次。</summary>
    public sealed class DualContractThing : IFoo, IBar, IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }

    /// <summary>与同类实例互相 Equals，用来钉住释放清单是按引用去重的。</summary>
    public sealed class EquatableDisposable : IFoo, IBar, IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;

        public override bool Equals(object? obj) => obj is EquatableDisposable;

        public override int GetHashCode() => 0;
    }

    /// <summary>两个释放接口都实现，用来钉住同步路径也优先走 DisposeAsync。</summary>
    public sealed class BothDisposable : IDisposable, IAsyncDisposable
    {
        public int SyncDisposeCount { get; private set; }

        public int AsyncDisposeCount { get; private set; }

        public void Dispose() => SyncDisposeCount++;

        public ValueTask DisposeAsync()
        {
            AsyncDisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>无依赖的可释放类型，并发那条压力测试用。</summary>
    public sealed class DisposableSelf : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    /// <summary>契约与实现是同一个类型，用于验证自注册那组重载。</summary>
    public sealed class SelfRegistered { }

    /// <summary>构造闸门：进构造函数时点亮 <see cref="Entered"/>，然后卡在 <see cref="Release"/> 上。</summary>
    public sealed class Gate
    {
        public ManualResetEventSlim Entered { get; } = new(false);

        public ManualResetEventSlim Release { get; } = new(false);
    }

    public sealed class GatedSingleton
    {
        public GatedSingleton(Gate gate)
        {
            gate.Entered.Set();
            gate.Release.Wait(TimeSpan.FromSeconds(10));
        }
    }

    public sealed class GatedScoped
    {
        public GatedScoped(Gate gate)
        {
            gate.Entered.Set();
            gate.Release.Wait(TimeSpan.FromSeconds(10));
        }
    }

    public sealed class GatedDisposable : IDisposable
    {
        public GatedDisposable(Gate gate)
        {
            gate.Entered.Set();
            gate.Release.Wait(TimeSpan.FromSeconds(10));
        }

        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    public sealed class SelfScoped { }

    public sealed class SelfTransient { }

    public sealed class SelfFactoryMade { }

    public interface IScoped { }

    public interface ISingletonNeedsScoped { }

    public sealed class ScopedThing : IScoped { }

    /// <summary>Transient，依赖一个 Scoped —— 用来钉作用域校验那条间接路径。</summary>
    public sealed class NeedsScoped
    {
        public NeedsScoped(IScoped scoped) => Scoped = scoped;

        public IScoped Scoped { get; }
    }

    public sealed class ScopedThing2 : IScoped { }

    public sealed class DisposableScoped : IScoped, IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    public interface IScopedNeedsSingleton { }

    public sealed class SingletonNeedsScoped : ISingletonNeedsScoped
    {
        public SingletonNeedsScoped(IScoped scoped) => Scoped = scoped;

        public IScoped Scoped { get; }
    }

    public sealed class ScopedNeedsSingleton : IScopedNeedsSingleton
    {
        public ScopedNeedsSingleton(IFoo foo) => Foo = foo;

        public IFoo Foo { get; }
    }

    public interface IA { }

    public interface IB { }

    public sealed class A : IA
    {
        public A(IB inner) => Inner = inner;

        public IB Inner { get; }
    }

    public sealed class B : IB
    {
        public B(IA inner) => Inner = inner;

        public IA Inner { get; }
    }

    public interface IWithDefault { }

    public interface IMissing { }

    public sealed class WithDefault : IWithDefault
    {
        public WithDefault(IMissing? dependency = null) => Dependency = dependency;

        public IMissing? Dependency { get; }
    }
}
