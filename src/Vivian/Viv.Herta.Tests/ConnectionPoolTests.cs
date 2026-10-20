using Viv.Fakes;
using Viv.Herta.Link.Hubs;

namespace Viv.Herta.Tests
{
    /// <summary>
    /// 身份三元组顺序是 AppId, SubjectId, UserId，三个都是 long，写反了没有编译期提示。
    /// </summary>
    public class ConnectionPoolTests
    {
        private const long SubjectId = 1;
        private const long UserId = 2;
        private const long AppId = 3;

        [Fact]
        public void Add_ThenGet_ReturnsConnection()
        {
            var pool = new ConnectionPool(new FakeHubContext());
            pool.Add("c1", appId: AppId, subjectId: SubjectId, userId: UserId);

            Assert.Equal(new[] { "c1" }, pool.GetConnectionIds(SubjectId, UserId));
            Assert.Equal(new[] { "c1" }, pool.GetConnectionIds(AppId, SubjectId, UserId));
            Assert.Empty(pool.GetConnectionIds(AppId, SubjectId, userId: 9));

            var info = Assert.Single(pool.GetConnections(SubjectId));
            Assert.Equal(("c1", AppId, SubjectId, UserId), (info.ConnectionId, info.AppId, info.SubjectId, info.UserId));
        }

        [Fact]
        public void Add_SameUserAcrossApps_GetByUserAggregates()
        {
            var pool = new ConnectionPool(new FakeHubContext());
            pool.Add("c1", appId: AppId, subjectId: SubjectId, userId: UserId);
            pool.Add("c2", appId: 9, subjectId: SubjectId, userId: UserId);

            // 按用户查不区分客户端，多端同步靠它
            Assert.Equal(new[] { "c1", "c2" }, pool.GetConnectionIds(SubjectId, UserId));
            Assert.Equal(new[] { "c1" }, pool.GetConnectionIds(AppId, SubjectId, UserId));
        }

        [Fact]
        public void Add_字段顺序不会串位()
        {
            // 三个 long 位置写反编译期毫无提示，这条把顺序钉死
            var pool = new ConnectionPool(new FakeHubContext());
            pool.Add("a", appId: 1, subjectId: 10, userId: 100);
            pool.Add("b", appId: 2, subjectId: 10, userId: 100);
            pool.Add("c", appId: 1, subjectId: 20, userId: 100);

            Assert.Equal(new[] { "a" }, pool.GetConnectionIds(1, 10, 100));
            Assert.Equal(new[] { "b" }, pool.GetConnectionIds(2, 10, 100));
            Assert.Equal(new[] { "c" }, pool.GetConnectionIds(1, 20, 100));

            // 按用户聚合只看 SubjectId + UserId
            Assert.Equal(2, pool.GetConnectionIds(10, 100).Count);
            Assert.DoesNotContain("a", pool.GetConnectionIds(20, 100));
        }

        [Fact]
        public void Remove_RemovesConnection()
        {
            var pool = new ConnectionPool(new FakeHubContext());
            pool.Add("c1", appId: AppId, subjectId: SubjectId, userId: UserId);
            pool.Remove("c1");

            Assert.Empty(pool.GetConnectionIds(SubjectId, UserId));
            Assert.Empty(pool.GetConnections(SubjectId));
        }

        [Fact]
        public void Remove_UnknownConnection_NoOp()
        {
            var pool = new ConnectionPool(new FakeHubContext());
            pool.Add("c1", appId: AppId, subjectId: SubjectId, userId: UserId);
            pool.Remove("unknown");

            Assert.Single(pool.GetConnectionIds(SubjectId, UserId));
        }

        [Fact]
        public async Task ForceDisconnect_RemovesAndSends()
        {
            var hub = new FakeHubContext();
            var pool = new ConnectionPool(hub);
            pool.Add("c1", appId: AppId, subjectId: SubjectId, userId: UserId);

            await pool.ForceDisconnectAsync("c1");

            var call = Assert.Single(hub.Calls);
            Assert.Equal("Client", call.TargetKind);
            Assert.Equal("c1", call.TargetId);
            Assert.Equal(HertaLinkClientMethods.ForceDisconnect, call.Method);
            Assert.Empty(pool.GetConnectionIds(SubjectId, UserId));
        }

        [Fact]
        public async Task ForceDisconnectUser_DisconnectsAllUserConnections()
        {
            var hub = new FakeHubContext();
            var pool = new ConnectionPool(hub);
            pool.Add("c1", appId: 3, subjectId: SubjectId, userId: UserId);
            pool.Add("c2", appId: 9, subjectId: SubjectId, userId: UserId);
            pool.Add("other", appId: 3, subjectId: SubjectId, userId: 99);

            await pool.ForceDisconnectUserAsync(SubjectId, UserId);

            Assert.Equal(2, hub.Calls.Count);
            Assert.All(hub.Calls, c => Assert.Equal(HertaLinkClientMethods.ForceDisconnect, c.Method));
            Assert.Empty(pool.GetConnectionIds(SubjectId, UserId));
            Assert.Single(pool.GetConnectionIds(SubjectId, 99));
        }

        [Fact]
        public async Task ForceDisconnectSubject_OnlyDisconnectsThatSubject()
        {
            var hub = new FakeHubContext();
            var pool = new ConnectionPool(hub);
            pool.Add("s1a", appId: 3, subjectId: 1, userId: 2);
            pool.Add("s1b", appId: 3, subjectId: 1, userId: 3);
            pool.Add("s2a", appId: 3, subjectId: 2, userId: 5);

            await pool.ForceDisconnectSubjectAsync(1);

            Assert.Equal(2, hub.Calls.Count);
            Assert.Single(pool.GetConnections(2));
        }

        [Fact]
        public void SweepExpired_只剔除超租约的连接()
        {
            var pool = new ConnectionPool(new FakeHubContext());
            pool.Add("c1", appId: 3, subjectId: 1, userId: 2);

            // 刚登记的不该被扫
            Assert.Empty(pool.SweepExpired(TimeSpan.FromHours(1)));
            Assert.Single(pool.GetConnectionIds(1, 2));

            // 负 ttl = 所有连接都算超时，不必真等
            Assert.Equal(new[] { "c1" }, pool.SweepExpired(TimeSpan.FromTicks(-1)));
            Assert.Empty(pool.GetConnectionIds(1, 2));
        }

        [Fact]
        public void SweepExpired_清掉租约同时清掉连接本身()
        {
            var pool = new ConnectionPool(new FakeHubContext());
            pool.Add("c1", appId: 3, subjectId: 1, userId: 2);

            pool.SweepExpired(TimeSpan.FromTicks(-1));

            // 连接表与租约必须一起清，只清一边会留下「查不到但还在」的幽灵
            Assert.Empty(pool.GetConnections(1));
            Assert.Empty(pool.SweepExpired(TimeSpan.FromTicks(-1)));
        }

        [Fact]
        public void Clear_EmptiesEverything()
        {
            var pool = new ConnectionPool(new FakeHubContext());
            pool.Add("c1", appId: 3, subjectId: 1, userId: 2);
            pool.Add("c2", appId: 3, subjectId: 1, userId: 3);

            pool.Clear();

            Assert.Empty(pool.GetConnections(1));
            Assert.Empty(pool.GetConnectionIds(1, 2));
        }
    }
}
