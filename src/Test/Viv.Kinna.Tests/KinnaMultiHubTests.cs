using System.Reflection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Viv.Fakes;
using Viv.Kinna.Connections;
using Viv.Kinna.Dispatch;
using Viv.Kinna.Extensions;
using Viv.Kinna.Options;

namespace Viv.Kinna.Tests
{
    /// <summary>
    /// 一个进程装多个 Hub，各套状态要分开。之前所有东西都注册成无标签单例，
    /// 第二个 Hub 的注册会被 TryAdd 吞掉，这里把这件事盯住。
    /// </summary>
    public class KinnaMultiHubTests
    {
        public sealed class HubA : Hub
        {
        }

        public sealed class HubB : Hub
        {
        }

        /// <summary>两个 Hub 共用的事件类型，用来验证 Pusher 不会跨 Hub 串用。</summary>
        public sealed class SharedEvent
        {
        }

        public sealed class PusherA : IKinnaPusher<HubA, SharedEvent>
        {
            public KinnaPushPlan? Plan(SharedEvent evt, KinnaPushContext? context) => null;
        }

        public sealed class PusherB : IKinnaPusher<HubB, SharedEvent>
        {
            public KinnaPushPlan? Plan(SharedEvent evt, KinnaPushContext? context) => null;
        }

        /// <summary>专供「同 Hub 同事件两个 Pusher」的用例：只有 HubC 会撞，别的 Hub 不受影响。</summary>
        public sealed class HubC : Hub
        {
        }

        public sealed class DupEvent
        {
        }

        public sealed class DupPusherA : IKinnaPusher<HubC, DupEvent>
        {
            public KinnaPushPlan? Plan(DupEvent evt, KinnaPushContext? context) => null;
        }

        public sealed class DupPusherB : IKinnaPusher<HubC, DupEvent>
        {
            public KinnaPushPlan? Plan(DupEvent evt, KinnaPushContext? context) => null;
        }

        /// <summary>一个 Pusher 都没有的 Hub：扫出来是空集，不该报错。</summary>
        public sealed class HubD : Hub
        {
        }

        private static IConfiguration Configuration() =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["VivOptions:KinnaOption:HubA:HubPath"] = "/a",
                    ["VivOptions:KinnaOption:HubA:Prefix"] = "a",
                    ["VivOptions:KinnaOption:HubB:HubPath"] = "/b",
                    ["VivOptions:KinnaOption:HubB:Prefix"] = "b"
                })
                .Build();

        private static ServiceProvider Build()
        {
            var services = new ServiceCollection();

            // 宿主里由宿主构建器提供；健康检查的内部服务依赖 ILogger<T>
            services.AddLogging();
            services.AddSingleton<IHubContext<HubA>>(new FakeHubContext<HubA>());
            services.AddSingleton<IHubContext<HubB>>(new FakeHubContext<HubB>());

            // 宿主里由 AddViv 注册，这里补一份，否则清扫器解析不出来
            services.AddSingleton<Viv.Log.ILoggerContract>(new RecordingLogger());

            // AddVivConfig 会把 VivOptions:KinnaOption 绑成这个实例
            services.AddSingleton(new KinnaOptions { EntryTtlSeconds = 90 });
            services.AddSingleton(Configuration());

            services.AddKinna<HubA>();
            services.AddKinna<HubB>();

            return services.BuildServiceProvider();
        }

        [Fact]
        public void 两个Hub各自拿到独立的Session()
        {
            using var provider = Build();

            var a = provider.GetRequiredService<IKinnaSession<HubA>>();
            var b = provider.GetRequiredService<IKinnaSession<HubB>>();

            Assert.NotSame(a, b);
            Assert.Equal("HubA", a.Hub);
            Assert.Equal("HubB", b.Hub);
        }

        [Fact]
        public void 两个Hub的连接表互不可见()
        {
            using var provider = Build();

            var a = provider.GetRequiredService<IKinnaSession<HubA>>();
            var b = provider.GetRequiredService<IKinnaSession<HubB>>();

            a.Pool.Add("c1", appId: 1, subjectId: 2, userId: 3);

            Assert.Single(a.Pool.GetConnectionIds(2, 3));
            // 共用池的话这里会查到 c1
            Assert.Empty(b.Pool.GetConnectionIds(2, 3));
        }

        [Fact]
        public void 配置按Hub分别覆盖()
        {
            using var provider = Build();

            var a = provider.GetRequiredService<IKinnaSession<HubA>>();
            var b = provider.GetRequiredService<IKinnaSession<HubB>>();

            Assert.Equal("/a", a.Options.HubPath);
            Assert.Equal("/b", b.Options.HubPath);
            // 没被覆盖的键回落到共享默认值
            Assert.Equal(90, a.Options.EntryTtlSeconds);

            // 前缀不同 → 群名不同，同一进程里不会串组
            Assert.Equal("a:10:group:20", a.Keys.GroupName(10, 20));
            Assert.Equal("b:10:group:20", b.Keys.GroupName(10, 20));
        }

        [Fact]
        public void 两个Hub都会注册自己的租约清扫()
        {
            using var provider = Build();

            var sweepers = provider.GetServices<IHostedService>()
                .Where(x => x.GetType().IsGenericType
                            && x.GetType().GetGenericTypeDefinition() == typeof(KinnaConnectionLeaseSweeper<>))
                .ToList();

            Assert.Equal(2, sweepers.Count);
            Assert.Contains(sweepers, x => x.GetType().GetGenericArguments()[0] == typeof(HubA));
            Assert.Contains(sweepers, x => x.GetType().GetGenericArguments()[0] == typeof(HubB));
        }

        [Fact]
        public void 两个Hub用同一事件类型时各拿各的Pusher()
        {
            // 扫描按 Pusher 声明里的 Hub 归类：两个 Hub 消费同一事件类型也各归各的，
            // 不存在「后注册的盖掉先注册的」——那正是裸 IKinnaPusher<TEvent> 会出的问题
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IHubContext<HubA>>(new FakeHubContext<HubA>());
            services.AddSingleton<IHubContext<HubB>>(new FakeHubContext<HubB>());
            services.AddSingleton<Viv.Log.ILoggerContract>(new RecordingLogger());

            services.AddKinna<HubA>();
            services.AddKinna<HubB>();
            services.AddKinnaPusher<HubA>();
            services.AddKinnaPusher<HubB>();

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            Assert.IsType<PusherA>(scope.ServiceProvider.GetRequiredService<IKinnaPusher<HubA, SharedEvent>>());
            Assert.IsType<PusherB>(scope.ServiceProvider.GetRequiredService<IKinnaPusher<HubB, SharedEvent>>());

            // 两个投递器都要能解析出来：注册闭合了才算真的可用
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IKinnaMessageDispatcher<HubA, SharedEvent>>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IKinnaMessageDispatcher<HubB, SharedEvent>>());

            // 注入具体类型与注入契约是同一个实例，别为每次注入再构造一份
            Assert.Same(
                scope.ServiceProvider.GetRequiredService<IKinnaPusher<HubA, SharedEvent>>(),
                scope.ServiceProvider.GetRequiredService<PusherA>());
        }

        [Fact]
        public void 同一个Hub同一个事件有两个Pusher时注册期就报错()
        {
            var services = new ServiceCollection();

            var ex = Assert.Throws<InvalidOperationException>(() => services.AddKinnaPusher<HubC>());

            // 报错要说清是哪两个类：只报「重复注册」还得自己去猜
            Assert.Contains(nameof(DupPusherA), ex.Message);
            Assert.Contains(nameof(DupPusherB), ex.Message);
        }

        [Fact]
        public void 重复扫描同一个Hub不翻倍()
        {
            var services = new ServiceCollection();

            services.AddKinnaPusher<HubA>();
            services.AddKinnaPusher<HubA>();
            // 显式把 Hub 所在程序集再传一遍，也不该多注册一份
            services.AddKinnaPusher<HubA>(Assembly.GetExecutingAssembly());

            Assert.Single(services, x => x.ServiceType == typeof(IKinnaPusher<HubA, SharedEvent>));
            Assert.Single(services, x => x.ServiceType == typeof(IKinnaMessageDispatcher<HubA, SharedEvent>));
        }

        [Fact]
        public void 没有Pusher的Hub扫描不报错()
        {
            var services = new ServiceCollection();

            services.AddKinnaPusher<HubD>();

            Assert.DoesNotContain(services, x => x.ServiceType.IsGenericType
                                                && x.ServiceType.GetGenericArguments()[0] == typeof(HubD));
        }

        [Fact]
        public void 同一个Hub重复AddKinna_不重复注册健康检查和后台服务()
        {
            // 多模块装配时同一个 Hub 被装两遍：AddHostedService 自己会去重，
            // 但 AddCheck 走的是 Configure 追加，不去重
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IHubContext<HubA>>(new FakeHubContext<HubA>());
            services.AddSingleton<Viv.Log.ILoggerContract>(new RecordingLogger());

            services.AddKinna<HubA>();
            services.AddKinna<HubA>();

            using var provider = services.BuildServiceProvider();

            var sweepers = provider.GetServices<IHostedService>()
                .Count(x => x.GetType().IsGenericType
                            && x.GetType().GetGenericTypeDefinition() == typeof(KinnaConnectionLeaseSweeper<>));
            Assert.Equal(1, sweepers);

            var registrations = provider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<
                    Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckServiceOptions>>()
                .Value.Registrations;

            Assert.Single(registrations, x => x.Name == "kinna:HubA");
        }
    }
}
