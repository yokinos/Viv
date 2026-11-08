using Viv.Kinna.Connections;

namespace Viv.Kinna.Tests
{
    /// <summary>
    /// 身份顺序是 AppId、SubjectId、UserId，三个都是 long，写反了编译不报错。
    /// </summary>
    public class KinnaConnectionPoolTests
    {
        private const long SubjectId = 1;
        private const long UserId = 2;
        private const long AppId = 3;

        [Fact]
        public void Add_ThenGet_ReturnsConnection()
        {
            var pool = new KinnaConnectionPool();
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
            var pool = new KinnaConnectionPool();
            pool.Add("c1", appId: AppId, subjectId: SubjectId, userId: UserId);
            pool.Add("c2", appId: 9, subjectId: SubjectId, userId: UserId);

            // 按用户查不区分客户端，多端同步靠它
            Assert.Equal(new[] { "c1", "c2" }, pool.GetConnectionIds(SubjectId, UserId));
            Assert.Equal(new[] { "c1" }, pool.GetConnectionIds(AppId, SubjectId, UserId));
        }

        [Fact]
        public void Add_字段顺序不会串位()
        {
            // 三个 long 写反了编译不报错，这条盯住顺序
            var pool = new KinnaConnectionPool();
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
            var pool = new KinnaConnectionPool();
            pool.Add("c1", appId: AppId, subjectId: SubjectId, userId: UserId);
            pool.Remove("c1");

            Assert.Empty(pool.GetConnectionIds(SubjectId, UserId));
            Assert.Empty(pool.GetConnectionIds(AppId, SubjectId, UserId));
            Assert.Empty(pool.GetConnections(SubjectId));
        }

        [Fact]
        public void Remove_只摘掉自己那一行()
        {
            // 同一个用户两个客户端，两条索引里各有一份；摘一个不能把另一个带走
            var pool = new KinnaConnectionPool();
            pool.Add("c1", appId: 3, subjectId: SubjectId, userId: UserId);
            pool.Add("c2", appId: 9, subjectId: SubjectId, userId: UserId);

            pool.Remove("c1");

            Assert.Equal(new[] { "c2" }, pool.GetConnectionIds(SubjectId, UserId));
            Assert.Empty(pool.GetConnectionIds(3, SubjectId, UserId));
            Assert.Equal(new[] { "c2" }, pool.GetConnectionIds(9, SubjectId, UserId));
        }

        [Fact]
        public void Remove_UnknownConnection_NoOp()
        {
            var pool = new KinnaConnectionPool();
            pool.Add("c1", appId: AppId, subjectId: SubjectId, userId: UserId);
            pool.Remove("unknown");

            Assert.Single(pool.GetConnectionIds(SubjectId, UserId));
        }

        [Fact]
        public void SweepExpired_只剔除超租约的连接()
        {
            var pool = new KinnaConnectionPool();
            pool.Add("c1", appId: 3, subjectId: 1, userId: 2);

            // 刚登记的不该被扫
            Assert.Empty(pool.SweepExpired(TimeSpan.FromHours(1)));
            Assert.Single(pool.GetConnectionIds(1, 2));

            // 负 ttl = 所有连接都算超时，不必真等
            Assert.Equal(new[] { "c1" }, pool.SweepExpired(TimeSpan.FromTicks(-1)).Select(x => x.ConnectionId));
            Assert.Empty(pool.GetConnectionIds(1, 2));
        }

        [Fact]
        public void SweepExpired_清掉租约同时清掉连接本身()
        {
            var pool = new KinnaConnectionPool();
            pool.Add("c1", appId: 3, subjectId: 1, userId: 2);

            pool.SweepExpired(TimeSpan.FromTicks(-1));

            // 连接表和租约要一起清，只清一边会剩下查不到又还在的记录
            Assert.Empty(pool.GetConnections(1));
            Assert.Empty(pool.SweepExpired(TimeSpan.FromTicks(-1)));
        }

        [Fact]
        public void SweepExpired_返回的身份够扣在线态()
        {
            var pool = new KinnaConnectionPool();
            pool.Add("c1", appId: 3, subjectId: 11, userId: 22);

            var swept = Assert.Single(pool.SweepExpired(TimeSpan.FromTicks(-1)));

            // 清扫完要调 presence.OfflineAsync，拿的就是这两个值
            Assert.Equal("c1", swept.ConnectionId);
            Assert.Equal(11, swept.SubjectId);
            Assert.Equal(22, swept.UserId);
        }

        [Fact]
        public void Clear_EmptiesEverything()
        {
            var pool = new KinnaConnectionPool();
            pool.Add("c1", appId: 3, subjectId: 1, userId: 2);
            pool.Add("c2", appId: 3, subjectId: 1, userId: 3);

            pool.Clear();

            Assert.Empty(pool.GetConnections(1));
            Assert.Empty(pool.GetConnectionIds(1, 2));
        }

        [Fact]
        public void Remove最后一条后再登记_新连接仍能被查到()
        {
            var pool = new KinnaConnectionPool();
            pool.Add("c1", appId: 3, subjectId: 1, userId: 2);
            pool.Remove("c1");
            pool.Add("c2", appId: 3, subjectId: 1, userId: 2);

            Assert.Equal(new[] { "c2" }, pool.GetConnectionIds(1, 2));
        }

        [Fact]
        public void Remove摘掉最后一条时_空桶跟着回收()
        {
            // 空桶不回收的话，每个注册过的用户都会永久占一本空字典：
            // 百万用户就是几百 MB，而且看不出来，因为连接数早就归零了
            var pool = new KinnaConnectionPool();
            pool.Add("c1", appId: 3, subjectId: 1, userId: 2);
            Assert.Equal(2, pool.BucketCount);

            pool.Remove("c1");

            Assert.Equal(0, pool.BucketCount);
        }

        [Fact]
        public void Remove同一用户的两条连接_只摘掉空了的那本桶()
        {
            var pool = new KinnaConnectionPool();
            pool.Add("c1", appId: 3, subjectId: 1, userId: 2);
            pool.Add("c2", appId: 3, subjectId: 1, userId: 2);

            pool.Remove("c1");

            // 按人那本还剩 c2，不能一起摘掉；按客户端那本也还剩 c2
            Assert.Equal(new[] { "c2" }, pool.GetConnectionIds(1, 2));
            Assert.Equal(2, pool.BucketCount);
        }

        [Fact]
        public void 并发登记和摘除_不丢连接也不留残桶()
        {
            var pool = new KinnaConnectionPool();

            Parallel.For(0, 200, i => pool.Add($"c{i}", appId: 3, subjectId: 1, userId: 2));
            Assert.Equal(200, pool.GetConnectionIds(1, 2).Count);

            Parallel.For(0, 200, i => pool.Remove($"c{i}"));

            Assert.Empty(pool.GetConnectionIds(1, 2));
            Assert.Equal(0, pool.BucketCount);
            Assert.Equal(0, pool.GetSnapshot().Connections);
        }
    }
}
