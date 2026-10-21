using Viv.EventContracts.Herta;
using Viv.Fakes;
using Viv.Herta.Link.Hubs;
using Viv.Kinna.Connections;
using Viv.Kinna.Contracts;
using Viv.Kinna.Options;

namespace Viv.Herta.Tests
{
    public class MembershipSyncTests
    {
        private const long TenantId = 1;
        private const long GroupId = 50;
        private const long UserId = 2;

        private static MembershipSync Create(KinnaConnectionPool pool, RecordingKinnaClientSender sender)
            => new(
                new StubKinnaSession<ChatHub>(pool, sender, new KinnaKeys("herta"), new KinnaOptions()),
                new RecordingLogger());

        [Fact]
        public async Task Joined_把本进程该用户的连接加进组()
        {
            var pool = new KinnaConnectionPool();
            var sender = new RecordingKinnaClientSender();
            pool.Add("c1", appId: 3, subjectId: TenantId, userId: UserId);

            await Create(pool, sender).ApplyAsync(new MembershipChangedEvent
            {
                TenantId = TenantId,
                GroupId = GroupId,
                UserIds = [UserId],
                ChangeType = MembershipChangeType.Joined
            });

            var added = Assert.Single(sender.Added);
            Assert.Equal("c1", added.ConnectionId);
            Assert.Equal("herta:1:group:50", added.Group);
        }

        [Fact]
        public async Task Left_把本进程该用户的连接移出组()
        {
            var pool = new KinnaConnectionPool();
            var sender = new RecordingKinnaClientSender();
            pool.Add("c1", appId: 3, subjectId: TenantId, userId: UserId);

            await Create(pool, sender).ApplyAsync(new MembershipChangedEvent
            {
                TenantId = TenantId,
                GroupId = GroupId,
                UserIds = [UserId],
                ChangeType = MembershipChangeType.Left
            });

            var removed = Assert.Single(sender.Removed);
            Assert.Equal("c1", removed.ConnectionId);
            Assert.Equal("herta:1:group:50", removed.Group);
        }

        [Fact]
        public async Task 用户不在本进程_不动任何连接()
        {
            var pool = new KinnaConnectionPool();
            var sender = new RecordingKinnaClientSender();

            await Create(pool, sender).ApplyAsync(new MembershipChangedEvent
            {
                TenantId = TenantId,
                GroupId = GroupId,
                UserIds = [UserId],
                ChangeType = MembershipChangeType.Joined
            });

            Assert.Empty(sender.Added);
        }
    }
}
