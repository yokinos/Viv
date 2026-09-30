using Microsoft.Extensions.DependencyInjection;
using Viv.Aoi.Paradox;

namespace Viv.Aoi.Tests
{
    /// <summary>
    /// VivContainer 与 MS DI 的桥接。这里用的 fallback 都是真容器
    /// （<see cref="ServiceCollection.BuildServiceProvider()"/>），不是替身 ——
    /// 要验的正是真容器那几条行为，替身会把它们和你以为的行为一起写出来。
    /// </summary>
    public class VivContainerMsDiTests
    {
        [Fact]
        public void 本地没注册的契约转交给fallback()
        {
            using var fallback = new ServiceCollection().AddSingleton<IFoo, Foo>().BuildServiceProvider();
            using var container = new VivContainer(fallback);

            Assert.IsType<Foo>(container.GetService(typeof(IFoo)));
        }

        [Fact]
        public void 本地注册的契约优先于fallback()
        {
            using var fallback = new ServiceCollection().AddSingleton<IFoo, FallbackFoo>().BuildServiceProvider();
            using var container = new VivContainer(fallback);
            container.AddSingleton<IFoo, Foo>();

            Assert.IsType<Foo>(container.GetService(typeof(IFoo)));
        }

        [Fact]
        public void 没有fallback时未注册契约返回空()
        {
            using var container = new VivContainer();

            Assert.Null(container.GetService(typeof(IFoo)));
        }

        [Fact]
        public void 两边都没有的契约返回空而不是抛异常()
        {
            using var fallback = new ServiceCollection().BuildServiceProvider();
            using var container = new VivContainer(fallback);

            Assert.Null(container.GetService(typeof(IFoo)));
        }

        [Fact]
        public void 两个Viv作用域从fallback拿到的Scoped不是同一个()
        {
            using var fallback = new ServiceCollection()
                .AddScoped<IFallbackScoped, FallbackScoped>()
                .BuildServiceProvider();
            using var container = new VivContainer(fallback);

            using var scope1 = container.CreateScope();
            using var scope2 = container.CreateScope();

            // 这是整个桥接最容易写错的一条：转交给根的 fallback 而不是各自那层，
            // 每个作用域拿到的就会是同一个实例，Scoped 静默退化成单例，毫无提示。
            Assert.NotSame(scope1.GetService<IFallbackScoped>(), scope2.GetService<IFallbackScoped>());
        }

        [Fact]
        public void 同一个Viv作用域里fallback的Scoped只创建一次()
        {
            using var fallback = new ServiceCollection()
                .AddScoped<IFallbackScoped, FallbackScoped>()
                .BuildServiceProvider();
            using var container = new VivContainer(fallback);

            using var scope = container.CreateScope();

            Assert.Same(scope.GetService<IFallbackScoped>(), scope.GetService<IFallbackScoped>());
        }

        [Fact]
        public void 释放Viv作用域会连fallback那层一起释放()
        {
            using var fallback = new ServiceCollection()
                .AddScoped<FallbackDisposable>()
                .BuildServiceProvider();
            var container = new VivContainer(fallback);

            FallbackDisposable resolved;
            using (var scope = container.CreateScope())
            {
                resolved = scope.GetService<FallbackDisposable>();
                Assert.False(resolved.Disposed);
            }

            Assert.True(resolved.Disposed);
            container.Dispose();
        }

        [Fact]
        public void 作用域上的Fallback是它自己那层而不是建根容器时传的那个()
        {
            using var fallback = new ServiceCollection().BuildServiceProvider();
            using var container = new VivContainer(fallback);

            Assert.Same(fallback, container.Fallback);

            using var scope = (VivContainer)container.CreateScope();

            Assert.NotNull(scope.Fallback);
            Assert.NotSame(fallback, scope.Fallback);
        }

        [Fact]
        public void 没有桥接时Fallback为空()
        {
            using var container = new VivContainer();
            using var scope = (VivContainer)container.CreateScope();

            Assert.Null(container.Fallback);
            Assert.Null(scope.Fallback);
        }

        [Fact]
        public void 释放Viv容器不会释放fallback本身()
        {
            using var fallback = new ServiceCollection().AddSingleton<IFoo, Foo>().BuildServiceProvider();
            var container = new VivContainer(fallback);

            container.Dispose();

            // fallback 是调用方的，容器只借不还。
            Assert.NotNull(fallback.GetService<IFoo>());
        }

        [Fact]
        public void 容器自身就是IServiceProvider()
        {
            using var container = new VivContainer();
            container.AddSingleton<IFoo, Foo>();

            IServiceProvider provider = container;

            Assert.IsType<Foo>(provider.GetService(typeof(IFoo)));
            Assert.Same(container, provider.GetService(typeof(IServiceProvider)));
        }

        [Fact]
        public void 作用域上解析容器契约拿到的是根容器()
        {
            using var container = new VivContainer();
            using var scope = container.CreateScope();

            IServiceProvider provider = scope;

            Assert.Same(container, provider.GetService(typeof(IVivContainer)));
            Assert.Same(scope, provider.GetService(typeof(IVivScope)));
        }

        [Fact]
        public void 作用域是完整的IServiceScope()
        {
            using var container = new VivContainer();
            container.AddScoped<IScoped, ScopedThing>();

            using var scope = container.CreateScope();

            IServiceScope msScope = scope;

            Assert.Same(scope, msScope.ServiceProvider.GetService(typeof(IVivScope)));
            Assert.NotNull(msScope.ServiceProvider.GetService(typeof(IScoped)));
        }

        [Theory]
        [InlineData(typeof(IServiceProvider))]
        [InlineData(typeof(IServiceProviderIsService))]
        [InlineData(typeof(ISupportRequiredService))]
        [InlineData(typeof(IVivContainer))]
        public void IsService认自身契约(Type contract)
        {
            using var container = new VivContainer();
            using var scope = container.CreateScope();

            Assert.True(container.IsService(contract));
            Assert.True(scope.IsService(contract));
        }

        [Fact]
        public void 作用域契约只认在作用域上()
        {
            using var container = new VivContainer();
            using var scope = container.CreateScope();

            // 根容器不是一层作用域。在它上面认下这两个契约、又给它实例的话，拿的人会
            // 把根当成随手可释放的一层，一个 using 就把整个容器连单例一起拆了。
            Assert.False(container.IsService(typeof(IServiceScope)));
            Assert.False(container.IsService(typeof(IVivScope)));
            Assert.Null(container.GetService(typeof(IServiceScope)));
            Assert.Null(container.GetService(typeof(IVivScope)));

            Assert.True(scope.IsService(typeof(IServiceScope)));
            Assert.True(scope.IsService(typeof(IVivScope)));
            Assert.Same(scope, scope.GetService(typeof(IVivScope)));
        }

        [Fact]
        public void IsService会问到fallback()
        {
            using var fallback = new ServiceCollection().AddSingleton<IFoo, Foo>().BuildServiceProvider();
            using var container = new VivContainer(fallback);

            Assert.True(container.IsService(typeof(IFoo)));
            Assert.False(container.IsService(typeof(IFallbackScoped)));
        }

        [Fact]
        public void IsService只问不建()
        {
            using var container = new VivContainer();
            container.AddTransient<CountingThing, CountingThing>();

            Assert.True(container.IsService(typeof(CountingThing)));
            Assert.Equal(0, CountingThing.Constructed);
        }

        [Fact]
        public void GetRequiredService解析得到实例()
        {
            using var container = new VivContainer();
            container.AddSingleton<IFoo, Foo>();

            Assert.IsType<Foo>(container.GetRequiredService(typeof(IFoo)));
        }

        [Fact]
        public void GetRequiredService未注册时抛异常()
        {
            using var container = new VivContainer();

            Assert.Throws<InvalidOperationException>(() => container.GetRequiredService(typeof(IFoo)));
        }

        [Fact]
        public void GetRequiredService能从fallback拿到()
        {
            using var fallback = new ServiceCollection().AddSingleton<IFoo, Foo>().BuildServiceProvider();
            using var container = new VivContainer(fallback);

            Assert.IsType<Foo>(container.GetRequiredService(typeof(IFoo)));
        }

        [Fact]
        public void 适配器透传解析与IsService()
        {
            var container = new VivContainer();
            container.AddSingleton<IFoo, Foo>();
            using var adapter = new ServiceProviderVivContainer(container);

            Assert.IsType<Foo>(adapter.GetService(typeof(IFoo)));
            Assert.IsType<Foo>(adapter.GetRequiredService(typeof(IFoo)));
            Assert.True(adapter.IsService(typeof(IFoo)));
            Assert.False(adapter.IsService(typeof(IFallbackScoped)));
        }

        [Fact]
        public void 适配器自身就是它提供的作用域()
        {
            var container = new VivContainer();
            using var adapter = new ServiceProviderVivContainer(container);

            IServiceScope msScope = adapter;

            Assert.Same(adapter, msScope.ServiceProvider);
        }

        [Fact]
        public void 适配器开出的作用域是完整MS_DI作用域()
        {
            var container = new VivContainer();
            container.AddScoped<IScoped, ScopedThing>();
            using var adapter = new ServiceProviderVivContainer(container);

            IServiceScopeFactory factory = adapter;
            using var scope = factory.CreateScope();

            Assert.NotNull(scope.ServiceProvider.GetService(typeof(IScoped)));
            Assert.Same(
                scope.ServiceProvider.GetService(typeof(IScoped)),
                scope.ServiceProvider.GetService(typeof(IScoped)));
        }

        [Fact]
        public void 适配器开出的两个作用域从fallback拿到的Scoped不是同一个()
        {
            using var fallback = new ServiceCollection()
                .AddScoped<IFallbackScoped, FallbackScoped>()
                .BuildServiceProvider();
            var container = new VivContainer(fallback);
            using var adapter = new ServiceProviderVivContainer(container);

            IServiceScopeFactory factory = adapter;
            using var scope1 = factory.CreateScope();
            using var scope2 = factory.CreateScope();

            Assert.NotSame(
                scope1.ServiceProvider.GetService(typeof(IFallbackScoped)),
                scope2.ServiceProvider.GetService(typeof(IFallbackScoped)));
        }

        [Fact]
        public void 适配器释放会连容器一起释放()
        {
            var container = new VivContainer();
            container.AddSingleton<IFoo, DisposableFoo>();

            var adapter = new ServiceProviderVivContainer(container);
            var foo = (DisposableFoo)adapter.GetRequiredService(typeof(IFoo));

            adapter.Dispose();

            Assert.True(foo.Disposed);
            Assert.Throws<ObjectDisposedException>(() => container.GetService(typeof(IFoo)));
        }
    }

    public interface IFallbackScoped { }

    public sealed class FallbackScoped : IFallbackScoped { }

    /// <summary>注册进 MS DI 那边，用来验证 Viv 作用域释放时会把 fallback 那层一并释放掉。</summary>
    public sealed class FallbackDisposable : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    /// <summary>计数构造次数，用来证明 IsService 不去建实例。</summary>
    public sealed class CountingThing
    {
        public static int Constructed;

        public CountingThing() => Interlocked.Increment(ref Constructed);
    }

    /// <summary>与 <see cref="Foo"/> 分开，用于验证本地注册压得过 fallback。</summary>
    public sealed class FallbackFoo : IFoo { }
}
