using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Viv.Fakes;
using Viv.Herta.Link.Adapters;
using Viv.Herta.Link.Extensions;
using Viv.Herta.Link.Hubs;
using Viv.Kinna.Connections;
using Viv.Kinna.Options;
using Viv.Kinna.Ports;

namespace Viv.Herta.Tests
{
    /// <summary>
    /// 一个进程里两个 Hub = 两套运行时。这里盯三件事：连接表不串、配置各取各的、
    /// 身份解析和准入都换成了 Herta 的实现而不是框架默认值。
    ///
    /// 最后一条最容易漏：新加 Hub 时只写 AddKinna&lt;THub&gt;()，配置能跑、推送也通，
    /// 但准入退回「只校验身份完整性」，Apex 的应用校验被绕过 —— 没有任何报错。
    /// </summary>
    public class LineHubWiringTests
    {
        /// <summary>只放每个 Hub 自己的那段；共享值等价于 AddVivConfig 绑出来的单例。</summary>
        private static IConfiguration Configuration() =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["VivOptions:KinnaOption:LineHub:HubPath"] = "/line",
                    ["VivOptions:KinnaOption:LineHub:WebSocketPath"] = "/line-ws",
                    ["VivOptions:KinnaOption:LineHub:Prefix"] = "herta-line"
                })
                .Build();

        private static ServiceProvider Build()
        {
            var services = new ServiceCollection();

            // 宿主里由 AddVivApi 提供
            services.AddLogging();
            services.AddSingleton<Viv.Log.ILoggerContract>(new RecordingLogger());
            services.AddSingleton(Configuration());
            services.AddSingleton(new KinnaOptions
            {
                HubPath = "/chat",
                WebSocketPath = "/chat-ws",
                Prefix = "herta"
            });

            services.AddHertaLink(Configuration());

            return services.BuildServiceProvider();
        }

        [Fact]
        public void 两个Hub各有一套会话和连接表()
        {
            using var provider = Build();

            var chat = provider.GetRequiredService<IKinnaSession<ChatHub>>();
            var line = provider.GetRequiredService<IKinnaSession<LineHub>>();

            Assert.NotSame(chat, line);
            Assert.NotSame(chat.Pool, line.Pool);

            chat.Pool.Add("c1", appId: 1, subjectId: 2, userId: 3);

            // 共用池的话这里会查到 c1：一个人从 Line 上线会被当成 Chat 在线
            Assert.Empty(line.Pool.GetConnectionIds(2, 3));
        }

        [Fact]
        public void 两个Hub的路径与前缀各取各的配置()
        {
            using var provider = Build();

            var chat = provider.GetRequiredService<IKinnaSession<ChatHub>>();
            var line = provider.GetRequiredService<IKinnaSession<LineHub>>();

            // 两条路径撞在一起 MapKinnaHub 会在启动时抛重复端点，所以这里必须不同
            Assert.Equal("/chat", chat.Options.HubPath);
            Assert.Equal("/line", line.Options.HubPath);
            Assert.Equal("/line-ws", line.Options.WebSocketPath);

            // 前缀不同 → 组名不同，同一租户的群在两个 Hub 里不会串
            Assert.NotEqual(chat.Keys.GroupName(10, 20), line.Keys.GroupName(10, 20));
        }

        [Fact]
        public void 两个Hub都用Herta的身份解析和准入()
        {
            var services = new ServiceCollection();

            services.AddHertaLink(Configuration());

            AssertLastImplementation<IKinnaIdentityResolver<ChatHub>, HertaIdentityResolver<ChatHub>>(services);
            AssertLastImplementation<IKinnaIdentityResolver<LineHub>, HertaIdentityResolver<LineHub>>(services);
            AssertLastImplementation<IKinnaAdmission<ChatHub>, HertaKinnaAdmission<ChatHub>>(services);
            AssertLastImplementation<IKinnaAdmission<LineHub>, HertaKinnaAdmission<LineHub>>(services);
        }

        /// <summary>Replace 之后生效的是最后一条，所以只看最后一条。</summary>
        private static void AssertLastImplementation<TService, TImplementation>(IServiceCollection services)
        {
            var descriptor = services.Last(x => x.ServiceType == typeof(TService));

            Assert.Equal(typeof(TImplementation), descriptor.ImplementationType);
        }
    }
}
