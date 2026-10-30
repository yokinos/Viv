using Viv.Contracts.Models;
using Viv.Fakes;
using Viv.Kinna.Connections;
using Viv.Kinna.Contracts;
using Viv.Kinna.Options;

namespace Viv.Kinna.Tests
{
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
            StubKinnaPresenceSink<TestHub>? presence = null,
            StubKinnaDeliveryStore<TestHub>? delivery = null,
            KinnaUplinkLimiter<TestHub>? limiter = null,
            bool singleConnectionPerApp = true,
            int entryTtlSeconds = 90)
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

            return new KinnaConnectionCoordinator<TestHub>(session, new RecordingLogger(), groups, presence, delivery, limiter);
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

            var watermark = await coordinator.AckAsync("c1", [11, 12]);

            Assert.Equal(12, watermark);
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

            Assert.Equal(0, second);
            Assert.Single(delivery.Acks);
        }

        [Fact]
        public async Task PingAsync_被上限挡下时仍回Pong但不续租()
        {
            var pool = new KinnaConnectionPool();
            var presence = new StubKinnaPresenceSink<TestHub>();
            var session = new StubKinnaSession<TestHub>(pool, options: new KinnaOptions { UplinkPermitLimit = 1 });
            using var limiter = new KinnaUplinkLimiter<TestHub>(session);

            var coordinator = Create(pool, new RecordingKinnaClientSender(), presence: presence, limiter: limiter);
            await coordinator.RegisterAsync("c1", Identity());

            var first = await coordinator.PingAsync("c1");
            var second = await coordinator.PingAsync("c1");

            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.Single(presence.RenewedUsers);
        }

        [Fact]
        public async Task AckAsync_未知连接不写()
        {
            var delivery = new StubKinnaDeliveryStore<TestHub>();
            var coordinator = Create(new KinnaConnectionPool(), new RecordingKinnaClientSender(), delivery: delivery);

            var watermark = await coordinator.AckAsync("ghost", [11]);

            Assert.Equal(0, watermark);
            Assert.Empty(delivery.Acks);
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
    }
}
