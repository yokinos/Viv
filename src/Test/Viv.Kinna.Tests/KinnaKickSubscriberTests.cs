using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Viv.Contracts.Models;
using Viv.Fakes;
using Viv.Kinna.Connections;
using Viv.Kinna.Contracts;
using Viv.Kinna.Extensions;
using Viv.Kinna.Options;
using Viv.Kinna.Ports;
using Viv.Log;

namespace Viv.Kinna.Tests
{
    /// <summary>
    /// 跨副本踢人的订阅端。模拟「另一个副本发了广播」：
    /// 通道替身投递指令，断言本机连接被踢、以及哪些消息该被跳过。
    /// </summary>
    public class KinnaKickSubscriberTests
    {
        private const long SubjectId = 1;
        private const long UserId = 2;
        private const long AppId = 3;

        private static VivContextContent Identity() =>
            new() { AppId = AppId, SubjectId = SubjectId, UserId = UserId };

        [Fact]
        public async Task 收到其他副本的指令_踢掉本机连接()
        {
            await using var fixture = await Fixture.StartAsync();
            await fixture.Coordinator.RegisterAsync("c1", Identity());
            Assert.Single(fixture.Pool.GetConnectionIds(SubjectId, UserId));

            await fixture.Bus.DeliverAsync(new KinnaKickCommand
            {
                SubjectId = SubjectId,
                UserId = UserId,
                Reason = "封号",
                Origin = "other-replica"
            });

            // 连接被摘表并收到强制下线——和本机直接 KickUserAsync 走同一条路
            Assert.Empty(fixture.Pool.GetConnectionIds(SubjectId, UserId));
            Assert.Contains(
                fixture.Sender.Calls,
                x => x.Method == KinnaClientMethods.ForceDisconnect && x.TargetId == "c1");
        }

        [Fact]
        public async Task 收到单端踢人指令_只踢该客户端()
        {
            // AppId 必须按消息里的值走：漏了就会把用户其它端一起断掉，
            // 而客户端重连是有成本的（重订阅 + Resync）
            await using var fixture = await Fixture.StartAsync();
            const long pcAppId = 3;   // 和 Identity() 里的 AppId 一致
            await fixture.Coordinator.RegisterAsync("pc", Identity());
            await fixture.Coordinator.RegisterAsync(
                "mini", new VivContextContent { AppId = 4, SubjectId = SubjectId, UserId = UserId });

            await fixture.Bus.DeliverAsync(new KinnaKickCommand
            {
                AppId = pcAppId,
                SubjectId = SubjectId,
                UserId = UserId,
                Origin = "other-replica"
            });

            Assert.Equal(new[] { "mini" }, fixture.Pool.GetConnectionIds(SubjectId, UserId));
            Assert.Contains(fixture.Sender.Calls, x => x.Method == KinnaClientMethods.ForceDisconnect && x.TargetId == "pc");
            Assert.DoesNotContain(fixture.Sender.Calls, x => x.TargetId == "mini");
        }

        [Fact]
        public async Task 收到整租户踢人_踢掉该租户下所有连接()
        {
            await using var fixture = await Fixture.StartAsync();
            await fixture.Coordinator.RegisterAsync("c1", Identity());
            await fixture.Coordinator.RegisterAsync(
                "c2", new VivContextContent { AppId = AppId, SubjectId = SubjectId, UserId = 9 });

            await fixture.Bus.DeliverAsync(new KinnaKickCommand
            {
                SubjectId = SubjectId,
                UserId = 0,
                Origin = "other-replica"
            });

            // UserId = 0 是「整个租户」的约定，两个用户都要断
            Assert.Empty(fixture.Pool.GetConnections(SubjectId));
        }

        [Fact]
        public async Task 收到按客户端踢人指令_踢掉该app的所有连接()
        {
            // 一个 AppId 往往服务多个租户：强制执行某一端（版本有 bug 要下架）时，
            // 这些连接不管属于谁都要断，其它 app 一条都不动
            await using var fixture = await Fixture.StartAsync();
            await fixture.Coordinator.RegisterAsync("pc-a", Identity());
            await fixture.Coordinator.RegisterAsync(
                "pc-b", new VivContextContent { AppId = AppId, SubjectId = 2, UserId = 7 });
            await fixture.Coordinator.RegisterAsync(
                "mini", new VivContextContent { AppId = 4, SubjectId = SubjectId, UserId = UserId });

            await fixture.Bus.DeliverAsync(new KinnaKickCommand
            {
                AppId = AppId,
                SubjectId = 0,
                UserId = 0,
                Reason = "强制升级",
                Origin = "other-replica"
            });

            // 同一个 app 的两条都断了，跨租户也断
            Assert.Empty(fixture.Pool.GetConnectionIdsByApp(AppId));

            // 别的 app 不受影响
            Assert.Equal(new[] { "mini" }, fixture.Pool.GetConnectionIdsByApp(4));
        }

        [Fact]
        public async Task 缺租户但带了用户_不能升级成按客户端踢()
        {
            // AppId 有值、UserId 也有值，但租户为空 —— 定位不了是哪个人，只能丢。
            // 这里判宽了就变成「踢掉这个 app 的所有人」，一条填错的消息能断掉一整个端
            await using var fixture = await Fixture.StartAsync();
            await fixture.Coordinator.RegisterAsync("c1", Identity());

            await fixture.Bus.DeliverAsync(new KinnaKickCommand
            {
                AppId = AppId,
                SubjectId = 0,
                UserId = UserId,
                Origin = "other-replica"
            });

            Assert.Single(fixture.Pool.GetConnectionIds(SubjectId, UserId));
            Assert.Empty(fixture.Sender.Calls);
        }

        [Fact]
        public async Task 跳过自己发布的指令_避免本机踢两遍()
        {
            await using var fixture = await Fixture.StartAsync();
            await fixture.Coordinator.RegisterAsync("c1", Identity());

            // 发布方已经先踢过本机，广播只是给别的副本的
            await fixture.Bus.DeliverAsync(new KinnaKickCommand
            {
                SubjectId = SubjectId,
                UserId = UserId,
                Origin = fixture.Bus.InstanceId
            });

            Assert.Single(fixture.Pool.GetConnectionIds(SubjectId, UserId));
            Assert.Empty(fixture.Sender.Calls);
        }

        [Fact]
        public async Task 没有租户的脏消息直接丢弃()
        {
            // 广播通道是共享的，别的 Hub 或写错的消息不该触发一次全表扫描
            await using var fixture = await Fixture.StartAsync();
            await fixture.Coordinator.RegisterAsync("c1", Identity());

            await fixture.Bus.DeliverAsync(new KinnaKickCommand { SubjectId = 0, UserId = UserId });

            Assert.Single(fixture.Pool.GetConnectionIds(SubjectId, UserId));
            Assert.Empty(fixture.Sender.Calls);
        }

        [Fact]
        public async Task 执行失败不抛出_否则订阅会静默失效()
        {
            // 回调跑在 Redis 的接收线程上，异常抛出去没人接，
            // 之后所有踢人指令都收不到——这才是这个用例要守的底线
            await using var fixture = await Fixture.StartAsync(throwOnKick: true);
            await fixture.Coordinator.RegisterAsync("c1", Identity());

            await fixture.Bus.DeliverAsync(new KinnaKickCommand
            {
                SubjectId = SubjectId,
                UserId = UserId,
                Origin = "other-replica"
            });

            // 订阅还在，通道还能继续用
            Assert.Equal(1, fixture.Bus.SubscriberCount);
        }

        [Fact]
        public async Task AddKinna_把广播通道接进协调器与订阅端()
        {
            // 协调器与订阅端都通过可选构造参数拿通道。DI 对「没有注册的可选参数」
            // 会用默认值，一旦谁把参数改成必填，这条用例先红，而不是等上线后
            // 单副本部署直接起不来
            var services = new ServiceCollection();
            services.AddLogging();   // 健康检查注册要 ILogger<T>
            services.AddSingleton<ILoggerContract>(new RecordingLogger());
            services.AddKinna<TestHub>();

            var bus = new RecordingKinnaKickBus<TestHub>();
            services.AddSingleton<IKinnaKickBus<TestHub>>(bus);

            await using var provider = services.BuildServiceProvider();

            // 订阅端注册成宿主服务，随进程起停
            Assert.Single(provider.GetServices<IHostedService>().OfType<KinnaKickSubscriber<TestHub>>());

            using var scope = provider.CreateScope();
            var coordinator = scope.ServiceProvider.GetRequiredService<IKinnaConnectionCoordinator<TestHub>>();
            await coordinator.RegisterAsync("c1", Identity());
            await coordinator.KickUserEverywhereAsync(SubjectId, UserId);

            // 通道被注入进去了：广播确实从协调器发出
            Assert.Single(bus.PublishedKicks);
        }

        [Fact]
        public async Task 未注册广播通道时整体空转_单副本部署不受影响()
        {
            var subscriber = new KinnaKickSubscriber<TestHub>(
                new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                new RecordingLogger());

            await subscriber.StartAsync(CancellationToken.None);
            await subscriber.StopAsync(CancellationToken.None);
            subscriber.Dispose();
        }

        [Fact]
        public async Task 释放后不再接收指令()
        {
            await using var fixture = await Fixture.StartAsync();
            await fixture.Coordinator.RegisterAsync("c1", Identity());

            await fixture.Subscriber.StopAsync(CancellationToken.None);
            fixture.Subscriber.Dispose();

            Assert.Equal(0, fixture.Bus.SubscriberCount);
            await fixture.Bus.DeliverAsync(new KinnaKickCommand
            {
                SubjectId = SubjectId,
                UserId = UserId,
                Origin = "other-replica"
            });

            Assert.Single(fixture.Pool.GetConnectionIds(SubjectId, UserId));
        }

        [Fact]
        public async Task 顶号指令只踢比它更旧的连接()
        {
            // 旧连接挂在别的副本上，那边只知道自己有这一条。指令里带了「新连接的建立时间」，
            // 它才敢踢；不带时间就只能全踢，而全踢会把并发上来的另一条新连接一起断掉
            var time = new FakeTimeProvider();
            await using var fixture = await Fixture.StartAsync(time: time);
            await fixture.Coordinator.RegisterAsync("old", Identity());

            time.Advance(TimeSpan.FromSeconds(5));

            await fixture.Bus.DeliverAsync(new KinnaKickCommand
            {
                AppId = AppId,
                SubjectId = SubjectId,
                UserId = UserId,
                EstablishedAt = time.GetUtcNow(),
                ConnectionId = "new-on-A",
                Reason = "顶号",
                Origin = "other-replica"
            });

            Assert.Empty(fixture.Pool.GetConnectionIds(SubjectId, UserId));
            Assert.Contains(
                fixture.Sender.Calls,
                x => x.Method == KinnaClientMethods.ForceDisconnect && x.TargetId == "old");
        }

        [Fact]
        public async Task 顶号不踢比自己更新的连接()
        {
            // 本机这条才刚连上，指令里那条比它旧 —— 该被顶掉的是发指令那边的前任，不是它
            var time = new FakeTimeProvider();
            await using var fixture = await Fixture.StartAsync(time: time);
            var olderCommandAt = time.GetUtcNow();

            time.Advance(TimeSpan.FromSeconds(5));
            await fixture.Coordinator.RegisterAsync("fresh", Identity());

            await fixture.Bus.DeliverAsync(new KinnaKickCommand
            {
                AppId = AppId,
                SubjectId = SubjectId,
                UserId = UserId,
                EstablishedAt = olderCommandAt,
                ConnectionId = "other-new",
                Reason = "顶号",
                Origin = "other-replica"
            });

            Assert.Equal(new[] { "fresh" }, fixture.Pool.GetConnectionIds(SubjectId, UserId));
            Assert.Empty(fixture.Sender.Calls);
        }

        [Fact]
        public async Task 同一时刻建立的两条连接_恰好只有一条被判为更旧()
        {
            // 只比时间的话：用 <= 两边都踢（互相顶掉、来回抖动），用 < 两边都不踢（留下两条）。
            // 带上连接 Id 之后任意两条之间必有且只有一个「更旧」，这是不抖动的根据
            var time = new FakeTimeProvider();
            await using var fixture = await Fixture.StartAsync(time: time);
            await fixture.Coordinator.RegisterAsync("a", Identity());

            // 对方那条叫 b：同一时刻，但 a < b，本机这条更旧
            await fixture.Bus.DeliverAsync(new KinnaKickCommand
            {
                AppId = AppId,
                SubjectId = SubjectId,
                UserId = UserId,
                EstablishedAt = time.GetUtcNow(),
                ConnectionId = "b",
                Origin = "other-replica"
            });

            Assert.Empty(fixture.Pool.GetConnectionIds(SubjectId, UserId));

            // 反过来：本机这条叫 c，对方那条叫 a —— c 更新，不该被踢
            await fixture.Coordinator.RegisterAsync("c", Identity());
            await fixture.Bus.DeliverAsync(new KinnaKickCommand
            {
                AppId = AppId,
                SubjectId = SubjectId,
                UserId = UserId,
                EstablishedAt = time.GetUtcNow(),
                ConnectionId = "a",
                Origin = "other-replica"
            });

            Assert.Equal(new[] { "c" }, fixture.Pool.GetConnectionIds(SubjectId, UserId));
            Assert.DoesNotContain(
                fixture.Sender.Calls,
                x => x.Method == KinnaClientMethods.ForceDisconnect && x.TargetId == "c");
        }

        [Fact]
        public async Task 管理动作不带建立时间_仍然全踢()
        {
            // 封号这类指令和顶号走同一条通道，不能被「只踢更旧的」逻辑带偏
            var time = new FakeTimeProvider();
            await using var fixture = await Fixture.StartAsync(time: time);
            await fixture.Coordinator.RegisterAsync("c1", Identity());

            await fixture.Bus.DeliverAsync(new KinnaKickCommand
            {
                AppId = AppId,
                SubjectId = SubjectId,
                UserId = UserId,
                Reason = "token 泄露",
                Origin = "other-replica"
            });

            Assert.Empty(fixture.Pool.GetConnectionIds(SubjectId, UserId));
        }

        private sealed class Fixture : IAsyncDisposable
        {
            private Fixture(
                KinnaKickSubscriber<TestHub> subscriber,
                RecordingKinnaKickBus<TestHub>? bus,
                KinnaConnectionCoordinator<TestHub> coordinator,
                KinnaConnectionPool pool,
                RecordingKinnaClientSender sender,
                ServiceProvider provider)
            {
                Subscriber = subscriber;
                Bus = bus!;
                Coordinator = coordinator;
                Pool = pool;
                Sender = sender;
                Provider = provider;
            }

            public KinnaKickSubscriber<TestHub> Subscriber { get; }

            public RecordingKinnaKickBus<TestHub> Bus { get; }

            public KinnaConnectionCoordinator<TestHub> Coordinator { get; }

            public KinnaConnectionPool Pool { get; }

            public RecordingKinnaClientSender Sender { get; }

            public ServiceProvider Provider { get; }

            /// <param name="throwOnKick">让踢人本身失败，验证异常被订阅端吞掉</param>
            /// <param name="time">建立时间的来源。顶号要比「谁更旧」，必须能拨时间</param>
            internal static async Task<Fixture> StartAsync(bool throwOnKick = false, TimeProvider? time = null)
            {
                var pool = new KinnaConnectionPool(timeProvider: time ?? TimeProvider.System);
                var sender = new RecordingKinnaClientSender();
                if (throwOnKick)
                {
                    // 注册只加组不发送，安全：到踢人那一步才会真的抛
                    sender.ThrowOnSend = new InvalidOperationException("sender down");
                }
                var log = new RecordingLogger();
                var coordinator = new KinnaConnectionCoordinator<TestHub>(
                    new StubKinnaSession<TestHub>(pool, sender, new KinnaKeys("kinna"), new KinnaOptions()),
                    log);

                var services = new ServiceCollection();
                services.AddScoped<IKinnaConnectionCoordinator<TestHub>>(_ => coordinator);
                var provider = services.BuildServiceProvider();

                var bus = new RecordingKinnaKickBus<TestHub>();
                var subscriber = new KinnaKickSubscriber<TestHub>(
                    provider.GetRequiredService<IServiceScopeFactory>(), log, bus);

                await subscriber.StartAsync(CancellationToken.None);

                // StartAsync 返回 ≠ 已经订阅上，等它真的登记完再返回，用例才不飘
                await bus.WhenSubscribedAsync(TimeSpan.FromSeconds(5));

                return new Fixture(subscriber, bus, coordinator, pool, sender, provider);
            }

            public async ValueTask DisposeAsync()
            {
                Subscriber.Dispose();
                await Provider.DisposeAsync();
            }
        }
    }
}
