using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Viv.Fakes;
using Viv.Kinna.Connections;
using Viv.Kinna.Extensions;

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

        private static IConfiguration Configuration() =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["KinnaOptions:EntryTtlSeconds"] = "90",
                    ["KinnaOptions:HubA:HubPath"] = "/a",
                    ["KinnaOptions:HubA:Prefix"] = "a",
                    ["KinnaOptions:HubB:HubPath"] = "/b",
                    ["KinnaOptions:HubB:Prefix"] = "b"
                })
                .Build();

        private static ServiceProvider Build()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IHubContext<HubA>>(new FakeHubContext<HubA>());
            services.AddSingleton<IHubContext<HubB>>(new FakeHubContext<HubB>());

            var configuration = Configuration();
            services.AddKinna<HubA>(configuration);
            services.AddKinna<HubB>(configuration);

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
    }
}
