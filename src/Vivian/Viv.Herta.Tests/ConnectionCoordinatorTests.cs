using Viv.Contracts.Models;
using Viv.Fakes;
using Viv.Herta.Link.Contracts;
using Viv.Herta.Link.Hubs;
using Viv.Herta.Link.Options;

namespace Viv.Herta.Tests
{
    public class ConnectionCoordinatorTests
    {
        private const long SubjectId = 1;
        private const long UserId = 2;
        private const long AppId = 3;

        private static VivContextContent Identity() => new() { AppId = AppId, SubjectId = SubjectId, UserId = UserId };

        private static ConnectionCoordinator Create(
            ConnectionPool pool,
            StubGroupService groups,
            FakeHubContext hub,
            int entryTtlSeconds = 90,
            StubPresenceStore? presence = null)
            => new(
                pool,
                groups,
                presence ?? new StubPresenceStore(),
                hub,
                XUnitTestMagic.CreateOptions(new HertaLinkOptions { EntryTtlSeconds = entryTtlSeconds }),
                new RecordingLogger());

        [Fact]
        public async Task RegisterAsync_登记连接并把该用户加入自己所在的群()
        {
            var hub = new FakeHubContext();
            var pool = new ConnectionPool(hub);
            var groups = new StubGroupService { GroupIds = new List<long> { 10, 20 } };
            var presence = new StubPresenceStore();

            var connected = await Create(pool, groups, hub, presence: presence).RegisterAsync("c1", Identity());

            Assert.NotNull(connected);
            Assert.Equal("c1", connected!.ConnectionId);
            Assert.Equal(new[] { "c1" }, pool.GetConnectionIds(SubjectId, UserId));

            var added = (FakeGroupManager)hub.Groups;
            Assert.Equal(
                new[] { HertaLinkKeys.GetGroupName(SubjectId, 10), HertaLinkKeys.GetGroupName(SubjectId, 20) },
                added.Added.Select(x => x.Group));

            Assert.Equal((SubjectId, UserId), Assert.Single(presence.OnlineUsers));
        }

        [Fact]
        public async Task RegisterAsync_身份不全时返回null且不登记()
        {
            var hub = new FakeHubContext();
            var pool = new ConnectionPool(hub);
            var presence = new StubPresenceStore();
            var coordinator = Create(pool, new StubGroupService(), hub, presence: presence);

            var connected = await coordinator.RegisterAsync("c1", new VivContextContent { AppId = AppId, SubjectId = 0, UserId = 0 });

            Assert.Null(connected);
            Assert.Empty(pool.GetConnections(SubjectId));
            Assert.Empty(presence.OnlineUsers);
        }

        [Fact]
        public async Task UnregisterAsync_移除连接并扣减在线态()
        {
            var hub = new FakeHubContext();
            var pool = new ConnectionPool(hub);
            var presence = new StubPresenceStore();
            var coordinator = Create(pool, new StubGroupService(), hub, presence: presence);
            await coordinator.RegisterAsync("c1", Identity());

            await coordinator.UnregisterAsync("c1");

            Assert.Empty(pool.GetConnectionIds(SubjectId, UserId));
            Assert.Equal((SubjectId, UserId), Assert.Single(presence.OfflineUsers));
        }

        [Fact]
        public async Task PingAsync_返回租约到期时间并续期在线态()
        {
            var hub = new FakeHubContext();
            var pool = new ConnectionPool(hub);
            var presence = new StubPresenceStore();
            var coordinator = Create(pool, new StubGroupService(), hub, entryTtlSeconds: 30, presence: presence);
            await coordinator.RegisterAsync("c1", Identity());

            var pong = await coordinator.PingAsync("c1");

            Assert.Equal(TimeSpan.FromSeconds(30), pong.LeaseExpiresAt - pong.ServerTime);
            Assert.Equal((SubjectId, UserId), Assert.Single(presence.RenewedUsers));
        }

        [Fact]
        public async Task PingAsync_未登记过的连接不会复活()
        {
            var hub = new FakeHubContext();
            var pool = new ConnectionPool(hub);
            var presence = new StubPresenceStore();
            var coordinator = Create(pool, new StubGroupService(), hub, presence: presence);
            await coordinator.RegisterAsync("c1", Identity());
            await coordinator.UnregisterAsync("c1");

            await coordinator.PingAsync("c1");

            Assert.Empty(pool.GetConnectionIds(SubjectId, UserId));
            // 连接已经不在了，不该再去续在线态
            Assert.Empty(presence.RenewedUsers);
        }

        [Fact]
        public void Resync_始终要求客户端补拉()
        {
            var hub = new FakeHubContext();
            var result = Create(new ConnectionPool(hub), new StubGroupService(), hub).Resync("c1", lastMessageId: 42);

            Assert.True(result.NeedBackfill);
            Assert.Null(result.LastDeliveredMessageId);
        }
    }
}
