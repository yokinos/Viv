using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Time.Testing;
using Viv.Contracts.Models;
using Viv.Fakes;
using Viv.Kinna.Connections;
using Viv.Kinna.Contracts;
using Viv.Kinna.Options;
using Viv.Kinna.Ports;

namespace Viv.Kinna.Tests
{
    /// <summary>替身：在线态写入必抛。用来验证它不是「能不能连上」的裁决方。</summary>
    internal sealed class ThrowingKinnaPresenceSink<THub> : IKinnaPresenceSink<THub> where THub : Hub
    {
        public Task OnlineAsync(long subjectId, long userId) => throw new InvalidOperationException("在线态存储不可用");

        public Task OfflineAsync(long subjectId, long userId) => throw new InvalidOperationException("在线态存储不可用");

        public Task RenewAsync(long subjectId, long userId) => throw new InvalidOperationException("在线态存储不可用");
    }

    /// <summary>登记、续租、加组、踢人。全部走注入，不需要 Hub.Context 夹具。</summary>
    public class KinnaConnectionCoordinatorTests
    {
        private const long SubjectId = 1;
        private const long UserId = 2;
        private const long AppId = 3;

        private static VivContextContent Identity() => new() { AppId = AppId, SubjectId = SubjectId, UserId = UserId };

        private static KinnaConnectionCoordinator<TestHub> Create(
            KinnaConnectionPool pool,
            RecordingKinnaClientSender sender,
            StubKinnaGroupSource<TestHub>? groups = null,
            IKinnaPresenceSink<TestHub>? presence = null,
            StubKinnaDeliveryStore<TestHub>? delivery = null,
            KinnaUplinkLimiter<TestHub>? limiter = null,
            bool singleConnectionPerApp = true,
            int entryTtlSeconds = 90,
            TimeProvider? time = null,
            RecordingKinnaKickBus<TestHub>? kickBus = null)
        {
            var session = new StubKinnaSession<TestHub>(
                pool,
                sender,
                new KinnaKeys("kinna"),
                new KinnaOptions
                {
                    EntryTtlSeconds = entryTtlSeconds,
                    SingleConnectionPerApp = singleConnectionPerApp
                });

            return new KinnaConnectionCoordinator<TestHub>(
                session, new RecordingLogger(), groups, presence, delivery, limiter, time, kickBus);
        }

        [Fact]
        public async Task RegisterAsync_登记连接并把该用户加入自己所在的群()
        {
            var pool = new KinnaConnectionPool();
            var sender = new RecordingKinnaClientSender();
            var groups = new StubKinnaGroupSource<TestHub> { GroupIds = new List<long> { 10, 20 } };
            var presence = new StubKinnaPresenceSink<TestHub>();

            var connected = await Create(pool, sender, groups, presence).RegisterAsync("c1", Identity());

            Assert.NotNull(connected);
            Assert.Equal("c1", connected!.ConnectionId);
            Assert.Equal(1, connected.V);
            Assert.Equal(new[] { "c1" }, pool.GetConnectionIds(SubjectId, UserId));

            Assert.Equal(
                new[] { "kinna:1:group:10", "kinna:1:group:20" },
                sender.Added.Select(x => x.Group));

            Assert.Equal((SubjectId, UserId), Assert.Single(presence.OnlineUsers));
        }

        [Fact]
        public async Task 在线态抛异常仍然建得起来连接()
        {
            // 在线态是展示信息，Herta 的实现自己吞了异常所以线上没炸；但端口契约以前没写这条，
            // 换一个会抛的实现就变成「新连接全建不起来」。框架对查群、投递存储、清扫都已经降级，
            // 唯独在线态把「能不能连上」的裁决权交给了实现
            var pool = new KinnaConnectionPool();
            var sender = new RecordingKinnaClientSender();

            var connected = await Create(pool, sender, presence: new ThrowingKinnaPresenceSink<TestHub>())
                .RegisterAsync("c1", Identity());

            Assert.NotNull(connected);
            Assert.Equal(new[] { "c1" }, pool.GetConnectionIds(SubjectId, UserId));
        }

        [Fact]
        public async Task 在线态抛异常仍然回Pong并续租()
        {
            // RenewAsync 直接挡在 Pong 前面：它一抛，所有心跳都拿不到 Pong，
            // 客户端集体判定掉线重连——比存储慢导致的 brownout 更彻底
            var pool = new KinnaConnectionPool();
            var sender = new RecordingKinnaClientSender();
            var coordinator = Create(
                pool, sender, presence: new ThrowingKinnaPresenceSink<TestHub>(), time: new FakeTimeProvider());

            await coordinator.RegisterAsync("c1", Identity());

            var pong = await coordinator.PingAsync("c1");

            Assert.True(pong.Alive);
            Assert.True(pong.LeaseExpiresAt > pong.ServerTime);
        }

        [Fact]
        public async Task 在线态抛异常仍然踢得掉连接()
        {
            // OfflineAsync 原来挡在 TerminateConnectionAsync 前面：一抛，旧连接就永远踢不掉，
            // 而「踢不掉」的表现是连接表已经摘了、客户端还以为连着的黑洞
            var pool = new KinnaConnectionPool();
            var sender = new RecordingKinnaClientSender();
            var coordinator = Create(pool, sender, presence: new ThrowingKinnaPresenceSink<TestHub>());

            await coordinator.RegisterAsync("c1", Identity());

            await coordinator.KickAsync("c1");

            Assert.Contains(
                sender.Calls,
                x => x.Method == KinnaClientMethods.ForceDisconnect && x.TargetId == "c1");
            Assert.Empty(pool.GetConnectionIds(SubjectId, UserId));
        }

        [Fact]
        public async Task RegisterAsync_同客户端重复登录_踢掉旧连接()
        {
            // 重装/断线重连会带新 connectionId 进来，旧的那条必须让位
            var pool = new KinnaConnectionPool();
            var sender = new RecordingKinnaClientSender();
            var presence = new StubKinnaPresenceSink<TestHub>();
            var coordinator = Create(pool, sender, presence: presence);

            await coordinator.RegisterAsync("c1", Identity());
            await coordinator.RegisterAsync("c2", Identity());

            Assert.Equal(new[] { "c2" }, pool.GetConnectionIds(SubjectId, UserId));
            Assert.Contains(sender.Calls, x => x.Method == KinnaClientMethods.ForceDisconnect && x.TargetId == "c1");
            // 旧连接下线、新连接上线，在线态先减后加
            Assert.Single(presence.OfflineUsers);
            Assert.Equal(2, presence.OnlineUsers.Count);
        }

        [Fact]
        public async Task RegisterAsync_不同客户端互不顶替()
        {
            var pool = new KinnaConnectionPool();
            var coordinator = Create(pool, new RecordingKinnaClientSender());

            await coordinator.RegisterAsync("phone", Identity());
            await coordinator.RegisterAsync("pc", new VivContextContent { AppId = 9, SubjectId = SubjectId, UserId = UserId });

            Assert.Equal(new[] { "phone" }, pool.GetConnectionIds(AppId, SubjectId, UserId));
            Assert.Equal(new[] { "pc" }, pool.GetConnectionIds(9, SubjectId, UserId));
            Assert.Equal(2, pool.GetConnectionIds(SubjectId, UserId).Count);
        }

        [Fact]
        public async Task RegisterAsync_关掉单连接策略时两条都留着()
        {
            var pool = new KinnaConnectionPool();
            var coordinator = Create(pool, new RecordingKinnaClientSender(), singleConnectionPerApp: false);

            await coordinator.RegisterAsync("c1", Identity());
            await coordinator.RegisterAsync("c2", Identity());

            Assert.Equal(2, pool.GetConnectionIds(AppId, SubjectId, UserId).Count);
        }

        [Fact]
        public async Task RegisterAsync_未注册群成员端口时连接照常建立()
        {
            // 不注册群成员端口不算错，只是群消息投不到人，单聊还能用
            var pool = new KinnaConnectionPool();
            var sender = new RecordingKinnaClientSender();

            var connected = await Create(pool, sender).RegisterAsync("c1", Identity());

            Assert.NotNull(connected);
            Assert.Empty(sender.Added);
        }

        [Fact]
        public async Task RegisterAsync_身份不全时返回null且不登记()
        {
            var pool = new KinnaConnectionPool();
            var sender = new RecordingKinnaClientSender();
            var presence = new StubKinnaPresenceSink<TestHub>();
            var coordinator = Create(pool, sender, presence: presence);

            var connected = await coordinator.RegisterAsync("c1", new VivContextContent { AppId = AppId, SubjectId = 0, UserId = 0 });

            Assert.Null(connected);
            Assert.Empty(pool.GetConnections(SubjectId));
            Assert.Empty(presence.OnlineUsers);
        }

        [Fact]
        public async Task UnregisterAsync_移除连接并扣减在线态()
        {
            var pool = new KinnaConnectionPool();
            var sender = new RecordingKinnaClientSender();
            var presence = new StubKinnaPresenceSink<TestHub>();
            var coordinator = Create(pool, sender, presence: presence);
            await coordinator.RegisterAsync("c1", Identity());

            await coordinator.UnregisterAsync("c1");

            Assert.Empty(pool.GetConnectionIds(SubjectId, UserId));
            Assert.Equal((SubjectId, UserId), Assert.Single(presence.OfflineUsers));
        }

        [Fact]
        public async Task PingAsync_返回租约到期时间并续在线态()
        {
            var pool = new KinnaConnectionPool();
            var sender = new RecordingKinnaClientSender();
            var presence = new StubKinnaPresenceSink<TestHub>();
            var coordinator = Create(pool, sender, presence: presence, entryTtlSeconds: 30);
            await coordinator.RegisterAsync("c1", Identity());

            var pong = await coordinator.PingAsync("c1");

            Assert.Equal(TimeSpan.FromSeconds(30), pong.LeaseExpiresAt - pong.ServerTime);
            Assert.Equal((SubjectId, UserId), Assert.Single(presence.RenewedUsers));
        }

        [Fact]
        public async Task PingAsync_未登记过的连接不会复活()
        {
            var pool = new KinnaConnectionPool();
            var sender = new RecordingKinnaClientSender();
            var presence = new StubKinnaPresenceSink<TestHub>();
            var coordinator = Create(pool, sender, presence: presence);
            await coordinator.RegisterAsync("c1", Identity());
            await coordinator.UnregisterAsync("c1");

            await coordinator.PingAsync("c1");

            Assert.Empty(pool.GetConnectionIds(SubjectId, UserId));
            // 连接已经不在了，不该再去续在线态
            Assert.Empty(presence.RenewedUsers);
        }

        [Fact]
        public async Task UnregisterAsync_重复调用不会重复扣在线态()
        {
            // 连接表摘除同时返回身份，第二次调用拿不到，就不会再扣一次
            var pool = new KinnaConnectionPool();
            var sender = new RecordingKinnaClientSender();
            var presence = new StubKinnaPresenceSink<TestHub>();
            var coordinator = Create(pool, sender, presence: presence);
            await coordinator.RegisterAsync("c1", Identity());

            await coordinator.UnregisterAsync("c1");
            await coordinator.UnregisterAsync("c1");

            Assert.Single(presence.OfflineUsers);
        }

        [Fact]
        public async Task AckAsync_按连接表里的身份记录水位()
        {
            var pool = new KinnaConnectionPool();
            var delivery = new StubKinnaDeliveryStore<TestHub>();
            var coordinator = Create(pool, new RecordingKinnaClientSender(), delivery: delivery);
            await coordinator.RegisterAsync("c1", Identity());

            var result = await coordinator.AckAsync("c1", [11, 12]);

            Assert.Equal(KinnaAckStatus.Accepted, result.Status);
            Assert.Equal(12, result.Watermark);
            var ack = Assert.Single(delivery.Acks);
            Assert.Equal(SubjectId, ack.SubjectId);
            Assert.Equal(UserId, ack.UserId);
            Assert.Equal([11, 12], ack.MessageIds);
        }

        [Fact]
        public async Task AckAsync_被上行配额挡下时不写()
        {
            // 丢一次确认不会丢消息：客户端下次补报，或者由 Resync 触发补拉
            var pool = new KinnaConnectionPool();
            var delivery = new StubKinnaDeliveryStore<TestHub>();
            var session = new StubKinnaSession<TestHub>(pool, options: new KinnaOptions { UplinkPermitLimit = 1 });
            using var limiter = new KinnaUplinkLimiter<TestHub>(session);

            var coordinator = Create(pool, new RecordingKinnaClientSender(), delivery: delivery, limiter: limiter);
            await coordinator.RegisterAsync("c1", Identity());

            await coordinator.AckAsync("c1", [11]);
            var second = await coordinator.AckAsync("c1", [12]);

            // 下限流要有明确的状态，不能和「未知连接」「落库失败」都回一个 0 混在一起
            Assert.Equal(KinnaAckStatus.Throttled, second.Status);

            // 只回 Throttled 不给时长，客户端只能猜「稍后」是多久
            Assert.InRange(second.RetryAfterMs, 1, 60_000);
            Assert.Single(delivery.Acks);
        }

        [Fact]
        public async Task PingAsync_被限流时回真实到期时间_不假装续过()
        {
            // 心跳走单独一档配额：配 1 次就不会被 Ack 挤掉
            var time = new FakeTimeProvider();
            var pool = new KinnaConnectionPool(nameof(TestHub), time);
            var presence = new StubKinnaPresenceSink<TestHub>();
            var session = new StubKinnaSession<TestHub>(
                pool,
                options: new KinnaOptions { UplinkHeartbeatPermitLimit = 1 });
            using var limiter = new KinnaUplinkLimiter<TestHub>(session);

            var coordinator = Create(pool, new RecordingKinnaClientSender(), presence: presence, limiter: limiter, time: time);
            await coordinator.RegisterAsync("c1", Identity());

            time.Advance(TimeSpan.FromSeconds(30));
            var first = await coordinator.PingAsync("c1");

            time.Advance(TimeSpan.FromSeconds(30));
            var second = await coordinator.PingAsync("c1");

            Assert.NotNull(first);
            Assert.NotNull(second);

            // 被挡的这次没有 Touch，租约到期的时刻一秒都没往后走。
            // 以前这里回的是 now + ttl，比真实值晚了一个心跳周期——
            // 客户端据此安排下一次心跳，就会在租约已经过期之后才来续，连接被清扫
            Assert.Equal(first.LeaseExpiresAt, second.LeaseExpiresAt);

            // 续期只发生在成功那次
            Assert.Single(presence.RenewedUsers);
        }

        [Fact]
        public async Task AckAsync_未知连接不写()
        {
            var delivery = new StubKinnaDeliveryStore<TestHub>();
            var coordinator = Create(new KinnaConnectionPool(), new RecordingKinnaClientSender(), delivery: delivery);

            var result = await coordinator.AckAsync("ghost", [11]);

            Assert.Equal(KinnaAckStatus.UnknownConnection, result.Status);
            Assert.Empty(delivery.Acks);
        }

        [Fact]
        public async Task AckAsync_落库失败返回Failed且不外抛()
        {
            var pool = new KinnaConnectionPool();
            var delivery = new StubKinnaDeliveryStore<TestHub>
            {
                ThrowOnAck = new InvalidOperationException("store down")
            };
            var coordinator = Create(pool, new RecordingKinnaClientSender(), delivery: delivery);
            await coordinator.RegisterAsync("c1", Identity());

            var result = await coordinator.AckAsync("c1", [11]);

            // 抛出去在 SignalR 里只是一次调用异常，客户端拿不到「该重报」这个信号
            Assert.Equal(KinnaAckStatus.Failed, result.Status);
        }

        [Fact]
        public async Task ResyncAsync_被限流时不查存储也不要求补拉()
        {
            var pool = new KinnaConnectionPool();
            var delivery = new StubKinnaDeliveryStore<TestHub>
            {
                Watermark = 20,
                ThrowOnGet = new InvalidOperationException("被限流时不该读存储")
            };
            var session = new StubKinnaSession<TestHub>(pool, options: new KinnaOptions { UplinkPermitLimit = 1 });
            using var limiter = new KinnaUplinkLimiter<TestHub>(session);

            var coordinator = Create(pool, new RecordingKinnaClientSender(), delivery: delivery, limiter: limiter);
            await coordinator.RegisterAsync("c1", Identity());

            // 用掉唯一的命令名额（Ping 走另一档，消耗不到它），Resync 就该被挡在查存储之前
            await coordinator.AckAsync("c1", [1]);
            var result = await coordinator.ResyncAsync("c1", lastMessageId: 10);

            Assert.True(result.Throttled);

            // 不能给补拉信号：那样客户端会立刻去打 HTTP，把压力从长连接搬到数据库
            Assert.False(result.NeedBackfill);

            // 退避时长是客户端唯一能据以等待的信息
            Assert.InRange(result.RetryAfterMs, 1, 60_000);
        }

        [Fact]
        public async Task AckAsync_逐条记下消息Id()
        {
            var pool = new KinnaConnectionPool();
            var delivery = new StubKinnaDeliveryStore<TestHub>();
            var coordinator = Create(pool, new RecordingKinnaClientSender(), delivery: delivery);
            await coordinator.RegisterAsync("c1", Identity());

            await coordinator.AckAsync("c1", [11, 12]);

            Assert.Equal([11, 12], delivery.MessageIds);
            Assert.Equal(12, delivery.Watermark);
        }

        [Fact]
        public async Task AckAsync_记满之后只留水位()
        {
            // 超过上限就全清，等价于「全部标记已收到」
            var pool = new KinnaConnectionPool();
            var delivery = new StubKinnaDeliveryStore<TestHub> { MaxMessageIds = 2 };
            var coordinator = Create(pool, new RecordingKinnaClientSender(), delivery: delivery);
            await coordinator.RegisterAsync("c1", Identity());

            await coordinator.AckAsync("c1", [11]);
            await coordinator.AckAsync("c1", [12]);
            await coordinator.AckAsync("c1", [13]);

            Assert.Empty(delivery.MessageIds);
            Assert.Equal(13, delivery.Watermark);
        }

        [Fact]
        public async Task ResyncAsync_回报已确认的消息Id()
        {
            var pool = new KinnaConnectionPool();
            var delivery = new StubKinnaDeliveryStore<TestHub>();
            var coordinator = Create(pool, new RecordingKinnaClientSender(), delivery: delivery);
            await coordinator.RegisterAsync("c1", Identity());
            await coordinator.AckAsync("c1", [11, 12]);

            var result = await coordinator.ResyncAsync("c1", lastMessageId: 10);

            Assert.True(result.NeedBackfill);
            Assert.Equal(12, result.LastDeliveredMessageId);
            Assert.Equal([11, 12], result.DeliveredMessageIds);
        }

        [Fact]
        public async Task ResyncAsync_服务端水位更大_要求补拉()
        {
            var pool = new KinnaConnectionPool();
            var delivery = new StubKinnaDeliveryStore<TestHub> { Watermark = 20 };
            var coordinator = Create(pool, new RecordingKinnaClientSender(), delivery: delivery);
            await coordinator.RegisterAsync("c1", Identity());

            var result = await coordinator.ResyncAsync("c1", lastMessageId: 10);

            Assert.True(result.NeedBackfill);
            Assert.Equal(20, result.LastDeliveredMessageId);
        }

        [Fact]
        public async Task ResyncAsync_客户端已跟上_不需要补拉()
        {
            var pool = new KinnaConnectionPool();
            var delivery = new StubKinnaDeliveryStore<TestHub> { Watermark = 20 };
            var coordinator = Create(pool, new RecordingKinnaClientSender(), delivery: delivery);
            await coordinator.RegisterAsync("c1", Identity());

            var result = await coordinator.ResyncAsync("c1", lastMessageId: 20);

            Assert.False(result.NeedBackfill);
        }

        [Fact]
        public async Task ResyncAsync_没有水位记录_保守要求补拉()
        {
            var pool = new KinnaConnectionPool();
            var coordinator = Create(pool, new RecordingKinnaClientSender(), delivery: new StubKinnaDeliveryStore<TestHub>());
            await coordinator.RegisterAsync("c1", Identity());

            var result = await coordinator.ResyncAsync("c1", lastMessageId: 99);

            Assert.True(result.NeedBackfill);
            Assert.Null(result.LastDeliveredMessageId);
        }

        [Fact]
        public async Task ResyncAsync_读存储失败_降级成要求补拉而不是抛出去()
        {
            // 抛出去的后果：SignalR 里是一次调用异常，原生 WebSocket 里读循环没有 catch-all，
            // Kestrel 只会在 101 之后 abort —— 客户端看到的是毫无解释的断线
            var pool = new KinnaConnectionPool();
            var delivery = new StubKinnaDeliveryStore<TestHub>
            {
                ThrowOnGet = new InvalidOperationException("db down")
            };
            var coordinator = Create(pool, new RecordingKinnaClientSender(), delivery: delivery);
            await coordinator.RegisterAsync("c1", Identity());

            var result = await coordinator.ResyncAsync("c1", lastMessageId: 99);

            // 读不到水位就当作「可能缺消息」：增量拉取幂等，多拉一次比漏消息便宜
            Assert.True(result.NeedBackfill);
            Assert.False(result.Throttled);
            Assert.Null(result.LastDeliveredMessageId);
            Assert.Empty(result.DeliveredMessageIds);
        }

        [Fact]
        public async Task KickAsync_先移出连接表再下发软踢()
        {
            var pool = new KinnaConnectionPool();
            var sender = new RecordingKinnaClientSender();
            var coordinator = Create(pool, sender);
            await coordinator.RegisterAsync("c1", Identity());

            await coordinator.KickAsync("c1");

            var call = Assert.Single(sender.Calls);
            Assert.Equal("Client", call.TargetKind);
            Assert.Equal("c1", call.TargetId);
            Assert.Equal(KinnaClientMethods.ForceDisconnect, call.Method);
            Assert.Empty(pool.GetConnectionIds(SubjectId, UserId));
        }

        [Fact]
        public async Task KickAsync_摘表的同时扣在线态()
        {
            var pool = new KinnaConnectionPool();
            var presence = new StubKinnaPresenceSink<TestHub>();
            var coordinator = Create(pool, new RecordingKinnaClientSender(), presence: presence);
            await coordinator.RegisterAsync("c1", Identity());

            await coordinator.KickAsync("c1");

            Assert.Equal((SubjectId, UserId), Assert.Single(presence.OfflineUsers));
            Assert.Empty(pool.GetConnectionIds(SubjectId, UserId));
        }

        [Fact]
        public async Task KickUserAsync_踢掉该用户全部连接()
        {
            var pool = new KinnaConnectionPool();
            var sender = new RecordingKinnaClientSender();
            var coordinator = Create(pool, sender);
            await coordinator.RegisterAsync("c1", Identity());
            pool.Add("c2", appId: 9, subjectId: SubjectId, userId: UserId);
            pool.Add("other", appId: AppId, subjectId: SubjectId, userId: 99);

            await coordinator.KickUserAsync(SubjectId, UserId);

            Assert.Equal(2, sender.Calls.Count);
            Assert.All(sender.Calls, c => Assert.Equal(KinnaClientMethods.ForceDisconnect, c.Method));
            Assert.Empty(pool.GetConnectionIds(SubjectId, UserId));
            Assert.Single(pool.GetConnectionIds(SubjectId, 99));
        }

        [Fact]
        public async Task KickSubjectAsync_只踢该租户的连接()
        {
            var pool = new KinnaConnectionPool();
            var sender = new RecordingKinnaClientSender();
            var coordinator = Create(pool, sender);
            pool.Add("s1a", appId: AppId, subjectId: 1, userId: 2);
            pool.Add("s1b", appId: AppId, subjectId: 1, userId: 3);
            pool.Add("s2a", appId: AppId, subjectId: 2, userId: 5);

            await coordinator.KickSubjectAsync(1);

            Assert.Equal(2, sender.Calls.Count);
            Assert.Single(pool.GetConnections(2));
        }

        [Fact]
        public async Task KickAppAsync_踢掉该app的全部连接_不分租户和用户()
        {
            // 强制执行某一端时，这个 app 的连接不管属于哪个租户、哪个用户都要断
            var pool = new KinnaConnectionPool();
            var sender = new RecordingKinnaClientSender();
            var coordinator = Create(pool, sender);

            pool.Add("a1", appId: AppId, subjectId: 1, userId: 2);
            pool.Add("a2", appId: AppId, subjectId: 2, userId: 5);
            pool.Add("a3", appId: AppId, subjectId: 1, userId: 3);
            pool.Add("b1", appId: 99, subjectId: 1, userId: 2);

            await coordinator.KickAppAsync(AppId);

            Assert.Equal(3, sender.Calls.Count);
            Assert.Empty(pool.GetConnectionIdsByApp(AppId));

            // 别的 app 一条都不动
            Assert.Equal(new[] { "b1" }, pool.GetConnectionIdsByApp(99));
        }

        [Fact]
        public async Task KickAppEverywhereAsync_本机先踢再广播_指令里不带租户()
        {
            var pool = new KinnaConnectionPool();
            var sender = new RecordingKinnaClientSender();
            var bus = new RecordingKinnaKickBus<TestHub>();
            var coordinator = Create(pool, sender, kickBus: bus);

            pool.Add("a1", appId: AppId, subjectId: 1, userId: 2);
            pool.Add("a2", appId: AppId, subjectId: 2, userId: 5);

            await coordinator.KickAppEverywhereAsync(AppId, "强制升级");

            Assert.Empty(pool.GetConnectionIdsByApp(AppId));

            var published = Assert.Single(bus.PublishedKicks);
            Assert.Equal(AppId, published.AppId);

            // 这是唯一允许不带租户的踢人指令：订阅端靠「AppId 有值 + 租户为空」认出这种粒度
            Assert.Equal(0, published.SubjectId);
            Assert.Equal("强制升级", published.Reason);
        }

        [Fact]
        public async Task KickAppAsync_非法AppId不做事()
        {
            var pool = new KinnaConnectionPool();
            var sender = new RecordingKinnaClientSender();
            var coordinator = Create(pool, sender);
            pool.Add("a1", appId: AppId, subjectId: 1, userId: 2);

            await coordinator.KickAppAsync(0);

            Assert.Empty(sender.Calls);
            Assert.Single(pool.GetConnectionIdsByApp(AppId));
        }

        [Fact]
        public async Task RegisterAsync_群查询失败时连接照常建立()
        {
            // 群服务抖一下不该让人连不上 IM：单聊和按人推送都还正常，只是这次没进组。
            // 之前这里会让 RegisterAsync 直接抛，等于把 IM 的可用性挂在群服务上。
            var pool = new KinnaConnectionPool();
            var sender = new RecordingKinnaClientSender();
            var groups = new StubKinnaGroupSource<TestHub> { ThrowOnGet = new InvalidOperationException("db down") };
            var logger = new RecordingLogger();
            var session = new StubKinnaSession<TestHub>(pool, sender, new KinnaKeys("kinna"), new KinnaOptions());
            var coordinator = new KinnaConnectionCoordinator<TestHub>(session, logger, groups);

            var connected = await coordinator.RegisterAsync("c1", Identity());

            Assert.NotNull(connected);
            Assert.Equal(new[] { "c1" }, pool.GetConnectionIds(SubjectId, UserId));
            Assert.Empty(sender.Added);
            Assert.Contains(logger.Warnings, message => message.Contains("不加组"));
        }

        [Fact]
        public async Task RegisterAsync_群查询被取消时不吞异常()
        {
            // 连接已经中止就别再继续加组：这时候吞掉取消信号会让调用方以为握手成功了
            var session = new StubKinnaSession<TestHub>(
                new KinnaConnectionPool(),
                new RecordingKinnaClientSender(),
                new KinnaKeys("kinna"),
                new KinnaOptions());

            var groups = new StubKinnaGroupSource<TestHub> { ThrowOnGet = new OperationCanceledException() };
            var coordinator = new KinnaConnectionCoordinator<TestHub>(session, new RecordingLogger(), groups);

            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => coordinator.RegisterAsync("c1", Identity(), cts.Token));
        }

        [Fact]
        public async Task PingAsync_连接不在表里时明确回报失效()
        {
            // 以前无论有没有连接都回 now + ttl，客户端拿到假保证，
            // 实际上这条连接已经收不到任何推送了 —— 「连着但不来消息」最难查的一种
            var pool = new KinnaConnectionPool();
            var presence = new StubKinnaPresenceSink<TestHub>();
            var coordinator = Create(pool, new RecordingKinnaClientSender(), presence: presence, entryTtlSeconds: 30);
            await coordinator.RegisterAsync("c1", Identity());
            await coordinator.UnregisterAsync("c1");

            var pong = await coordinator.PingAsync("c1");

            Assert.False(pong.Alive);
            Assert.Equal(pong.ServerTime, pong.LeaseExpiresAt);
            Assert.Empty(presence.RenewedUsers);
        }

        [Fact]
        public async Task PingAsync_正常连接回报续期后的租约()
        {
            var pool = new KinnaConnectionPool();
            var coordinator = Create(pool, new RecordingKinnaClientSender(), entryTtlSeconds: 30);
            await coordinator.RegisterAsync("c1", Identity());

            var pong = await coordinator.PingAsync("c1");

            Assert.True(pong.Alive);
            Assert.Equal(TimeSpan.FromSeconds(30), pong.LeaseExpiresAt - pong.ServerTime);
        }

        [Fact]
        public async Task UnregisterAsync_顺手摘掉上行限流窗口()
        {
            // 窗口按连接建，不摘的话每来一次重连就多一个，进程跑久了只增不减
            var pool = new KinnaConnectionPool();
            var session = new StubKinnaSession<TestHub>(pool, options: new KinnaOptions { UplinkPermitLimit = 5 });
            using var limiter = new KinnaUplinkLimiter<TestHub>(session);

            var coordinator = Create(pool, new RecordingKinnaClientSender(), limiter: limiter);
            await coordinator.RegisterAsync("c1", Identity());
            await coordinator.PingAsync("c1");
            Assert.Equal(1, limiter.TrackedConnections);

            await coordinator.UnregisterAsync("c1");

            Assert.Equal(0, limiter.TrackedConnections);
        }

        [Fact]
        public async Task 握手里的时刻来自注入的时钟()
        {
            // 客户端拿 ServerTime 和 LeaseExpiresAt 算自己该什么时候心跳。
            // 用系统墙钟的话，这两个值没法断言，也没法在测试里把租约推到快过期
            var time = new FakeTimeProvider();
            var pool = new KinnaConnectionPool(timeProvider: time);
            var coordinator = Create(pool, new RecordingKinnaClientSender(), entryTtlSeconds: 30, time: time);

            var connected = await coordinator.RegisterAsync("c1", Identity());
            Assert.Equal(time.GetUtcNow(), connected!.ServerTime);

            time.Advance(TimeSpan.FromSeconds(10));
            var pong = await coordinator.PingAsync("c1");

            Assert.Equal(time.GetUtcNow(), pong.ServerTime);
            Assert.Equal(TimeSpan.FromSeconds(30), pong.LeaseExpiresAt - pong.ServerTime);
        }

        [Fact]
        public async Task KickUserEverywhereAsync_先踢本机再广播_顺序不能反()
        {
            // 反过来（先广播后踢）在 Redis 慢的时候会让本机用户白多收一会儿消息，
            // 而「Redis 抖一下就封不了号」是最难解释的失败模式
            var pool = new KinnaConnectionPool();
            var sender = new RecordingKinnaClientSender();
            var bus = new RecordingKinnaKickBus<TestHub>();
            var coordinator = Create(pool, sender, kickBus: bus);

            await coordinator.RegisterAsync("c1", Identity());
            await coordinator.KickUserEverywhereAsync(SubjectId, UserId, "封号");

            // 本机连接已摘表并收到强制下线
            Assert.Empty(pool.GetConnectionIds(SubjectId, UserId));
            Assert.Contains(sender.Calls, x => x.Method == KinnaClientMethods.ForceDisconnect && x.TargetId == "c1");

            // 广播了一条带 UserId 的指令，Origin 由通道填
            var command = Assert.Single(bus.PublishedKicks);
            Assert.Equal(SubjectId, command.SubjectId);
            Assert.Equal(UserId, command.UserId);
            Assert.Equal("封号", command.Reason);
            Assert.Equal(bus.InstanceId, command.Origin);
        }

        [Fact]
        public async Task KickSubjectEverywhereAsync_广播的User为0表示整租户()
        {
            var pool = new KinnaConnectionPool();
            var bus = new RecordingKinnaKickBus<TestHub>();
            var coordinator = Create(pool, new RecordingKinnaClientSender(), kickBus: bus);

            await coordinator.RegisterAsync("c1", Identity());
            await coordinator.KickSubjectEverywhereAsync(SubjectId);

            Assert.Empty(pool.GetConnectionIds(SubjectId, UserId));

            // 0 是「整个租户」的约定，订阅端据此选 KickSubjectAsync。
            // 这里若换成实际 UserId，另一个副本就只会踢这一个用户
            var command = Assert.Single(bus.PublishedKicks);
            Assert.Equal(0, command.UserId);
        }

        [Fact]
        public async Task KickUserEverywhereAsync_广播失败不影响本机踢人也不抛()
        {
            // 调用方是封号接口，不该因为 Redis 抖动就报错回滚；
            // 其余副本靠 90s 租约兜底，最坏多连一会儿
            var pool = new KinnaConnectionPool();
            var sender = new RecordingKinnaClientSender();
            var bus = new RecordingKinnaKickBus<TestHub> { ThrowOnPublish = new InvalidOperationException("redis down") };
            var coordinator = Create(pool, sender, kickBus: bus);

            await coordinator.RegisterAsync("c1", Identity());

            await coordinator.KickUserEverywhereAsync(SubjectId, UserId);   // 不抛

            Assert.Empty(pool.GetConnectionIds(SubjectId, UserId));
            Assert.Contains(sender.Calls, x => x.Method == KinnaClientMethods.ForceDisconnect && x.TargetId == "c1");
        }

        [Fact]
        public async Task KickClientEverywhereAsync_只踢指定客户端_另一端照常在线()
        {
            // 某一端 token 泄露、某一端版本有问题时要单独踢；
            // 按 (SubjectId,UserId) 踢会把用户所有端一起断掉
            var pool = new KinnaConnectionPool();
            var sender = new RecordingKinnaClientSender();
            var bus = new RecordingKinnaKickBus<TestHub>();
            var coordinator = Create(pool, sender, kickBus: bus);

            const long miniAppId = 4;
            await coordinator.RegisterAsync("pc", Identity());
            await coordinator.RegisterAsync(
                "mini", new VivContextContent { AppId = miniAppId, SubjectId = SubjectId, UserId = UserId });

            await coordinator.KickClientEverywhereAsync(AppId, SubjectId, UserId, "token 泄露");

            Assert.Equal(new[] { "mini" }, pool.GetConnectionIds(SubjectId, UserId));
            Assert.Contains(sender.Calls, x => x.Method == KinnaClientMethods.ForceDisconnect && x.TargetId == "pc");
            Assert.DoesNotContain(sender.Calls, x => x.TargetId == "mini");

            // 广播要带上 AppId，否则别的副本会当成「踢所有端」
            var command = Assert.Single(bus.PublishedKicks);
            Assert.Equal(AppId, command.AppId);
            Assert.Equal(UserId, command.UserId);
        }

        [Fact]
        public async Task KickUserEverywhereAsync_覆盖该用户所有客户端()
        {
            // 封号的语义是全端：AppId 留 0，两端都得断
            var pool = new KinnaConnectionPool();
            var sender = new RecordingKinnaClientSender();
            var bus = new RecordingKinnaKickBus<TestHub>();
            var coordinator = Create(pool, sender, kickBus: bus);

            await coordinator.RegisterAsync("pc", Identity());
            await coordinator.RegisterAsync(
                "mini", new VivContextContent { AppId = 4, SubjectId = SubjectId, UserId = UserId });

            await coordinator.KickUserEverywhereAsync(SubjectId, UserId, "封号");

            Assert.Empty(pool.GetConnectionIds(SubjectId, UserId));
            Assert.Equal(0, Assert.Single(bus.PublishedKicks).AppId);
        }

        [Fact]
        public async Task RegisterAsync_广播顶号_带AppId和建立时间()
        {
            // 旧连接可能挂在别的副本上，本机这个循环踢不到它，只能广播。
            // AppId 必须带上：留 0 会被订阅端当成「踢这个人所有端」，别的设备白掉线
            var pool = new KinnaConnectionPool();
            var bus = new RecordingKinnaKickBus<TestHub>();
            var coordinator = Create(pool, new RecordingKinnaClientSender(), kickBus: bus);

            await coordinator.RegisterAsync("c1", Identity());

            var command = Assert.Single(bus.Published);
            Assert.Equal(AppId, command.AppId);
            Assert.Equal(SubjectId, command.SubjectId);
            Assert.Equal(UserId, command.UserId);
            Assert.Equal("c1", command.ConnectionId);
            Assert.NotNull(command.EstablishedAt);
            Assert.Equal(bus.InstanceId, command.Origin);
        }

        [Fact]
        public async Task RegisterAsync_关掉单连接策略时_不广播顶号()
        {
            // 允许多条连接就谈不上顶号，广播只会让别的副本白踢一趟
            var pool = new KinnaConnectionPool();
            var bus = new RecordingKinnaKickBus<TestHub>();
            var coordinator = Create(
                pool, new RecordingKinnaClientSender(), singleConnectionPerApp: false, kickBus: bus);

            await coordinator.RegisterAsync("c1", Identity());

            Assert.Empty(bus.Published);
        }

        [Fact]
        public async Task KickUserEverywhereAsync_没注册广播通道时退化成只踢本机()
        {
            // 单副本部署不该被迫接 Redis；没接的时候调用方代码不用改
            var pool = new KinnaConnectionPool();
            var sender = new RecordingKinnaClientSender();
            var coordinator = Create(pool, sender);

            await coordinator.RegisterAsync("c1", Identity());
            await coordinator.KickUserEverywhereAsync(SubjectId, UserId);

            Assert.Empty(pool.GetConnectionIds(SubjectId, UserId));
            Assert.Contains(sender.Calls, x => x.Method == KinnaClientMethods.ForceDisconnect);
        }

        [Fact]
        public async Task KickUserEverywhereAsync_单条软踢失败不影响其余连接和广播()
        {
            // 传输层抛一次（背板抖、对端已断）不能让整批踢人停在半路：
            // 剩下的本机连接还连着，广播也发不出去——对「封号」来说这是最糟的失败模式
            var pool = new KinnaConnectionPool();
            var sender = new RecordingKinnaClientSender();
            var bus = new RecordingKinnaKickBus<TestHub>();
            var coordinator = Create(pool, sender, kickBus: bus);

            await coordinator.RegisterAsync("c1", Identity());
            pool.Add("c2", appId: AppId, subjectId: SubjectId, userId: UserId);

            sender.ThrowOnSend = new InvalidOperationException("传输层抖动");

            await coordinator.KickUserEverywhereAsync(SubjectId, UserId);

            // 两条都已摘表，广播照发
            Assert.Empty(pool.GetConnectionIds(SubjectId, UserId));
            Assert.Single(bus.PublishedKicks);
        }

        [Fact]
        public async Task RegisterAsync_同客户端并发注册只留下一条()
        {
            // 「查旧连接」和「登记」分开做的话，两条连接会互相看不见对方、都成功登记；
            // 之后各自广播的顶号指令又被本副本按 Origin==自己 跳过，没人来收这条
            var pool = new KinnaConnectionPool();
            var coordinator = Create(pool, new RecordingKinnaClientSender());

            var registrations = Enumerable.Range(0, 20)
                .Select(i => coordinator.RegisterAsync($"c{i}", Identity()))
                .ToArray();

            await Task.WhenAll(registrations);

            Assert.Single(pool.GetConnectionIds(AppId, SubjectId, UserId));
        }

        [Fact]
        public async Task RegisterAsync_在线态被取消时不吞异常()
        {
            // 取消不是「在线态抖了一下」：吞掉之后会在一条已经作废的连接上继续握手、加组
            var pool = new KinnaConnectionPool();
            var presence = new CancelingKinnaPresenceSink<TestHub>();
            var coordinator = Create(pool, new RecordingKinnaClientSender(), presence: presence);

            using var cts = new CancellationTokenSource();
            presence.Source = cts;

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => coordinator.RegisterAsync("c1", Identity(), cts.Token));

            Assert.Contains((SubjectId, UserId), presence.OnlineUsers);
        }
    }

    /// <summary>替身：上线时把调用方的令牌置为取消，再抛取消——模拟「请求在写在线态时被中止」。</summary>
    internal sealed class CancelingKinnaPresenceSink<THub> : IKinnaPresenceSink<THub> where THub : Hub
    {
        public CancellationTokenSource? Source { get; set; }

        public List<(long SubjectId, long UserId)> OnlineUsers { get; } = new();

        public Task OnlineAsync(long subjectId, long userId)
        {
            OnlineUsers.Add((subjectId, userId));
            Source?.Cancel();
            throw new OperationCanceledException();
        }

        public Task OfflineAsync(long subjectId, long userId) => Task.CompletedTask;

        public Task RenewAsync(long subjectId, long userId) => Task.CompletedTask;
    }
}
