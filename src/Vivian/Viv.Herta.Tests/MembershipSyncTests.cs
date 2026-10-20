using Viv.EventContracts.Herta;
using Viv.Fakes;
using Viv.Herta.Link.Contracts;
using Viv.Herta.Link.Hubs;

namespace Viv.Herta.Tests
{
    public class MembershipSyncTests
    {
        private const long TenantId = 1;
        private const long GroupId = 50;
        private const long UserId = 2;

        private static MembershipSync Create(ConnectionPool pool, FakeHubContext hub)
            => new(pool, hub, new RecordingLogger());

        [Fact]
        public async Task Joined_把本进程该用户的连接加进组()
        {
            var hub = new FakeHubContext();
            var pool = new ConnectionPool(hub);
            pool.Add("c1", appId: 3, subjectId: TenantId, userId: UserId);

            await Create(pool, hub).ApplyAsync(new MembershipChangedEvent
            {
                TenantId = TenantId,
                GroupId = GroupId,
                UserIds = [UserId],
                ChangeType = MembershipChangeType.Joined
            });

            var added = Assert.Single(((FakeGroupManager)hub.Groups).Added);
            Assert.Equal("c1", added.ConnectionId);
            Assert.Equal(HertaLinkKeys.GetGroupName(TenantId, GroupId), added.Group);
        }

        [Fact]
        public async Task Left_把本进程该用户的连接移出组()
        {
            var hub = new FakeHubContext();
            var pool = new ConnectionPool(hub);
            pool.Add("c1", appId: 3, subjectId: TenantId, userId: UserId);

            await Create(pool, hub).ApplyAsync(new MembershipChangedEvent
            {
                TenantId = TenantId,
                GroupId = GroupId,
                UserIds = [UserId],
                ChangeType = MembershipChangeType.Left
            });

            var removed = Assert.Single(((FakeGroupManager)hub.Groups).Removed);
            Assert.Equal("c1", removed.ConnectionId);
            Assert.Equal(HertaLinkKeys.GetGroupName(TenantId, GroupId), removed.Group);
        }

        [Fact]
        public async Task 用户不在本进程_不动任何连接()
        {
            var hub = new FakeHubContext();
            var pool = new ConnectionPool(hub);

            await Create(pool, hub).ApplyAsync(new MembershipChangedEvent
            {
                TenantId = TenantId,
                GroupId = GroupId,
                UserIds = [UserId],
                ChangeType = MembershipChangeType.Joined
            });

            Assert.Empty(((FakeGroupManager)hub.Groups).Added);
        }
    }
}
