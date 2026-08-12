using System.Threading.Tasks;
using Viv.Herta.Link.Hubs;

namespace Viv.Herta.Tests
{
    public class ConnectionPoolTests
    {
        [Fact]
        public void Add_ThenGet_ReturnsConnection()
        {
            var pool = new ConnectionPool(new FakeHubContext());
            pool.Add("c1", 1, 2, 3);

            Assert.Equal(new[] { "c1" }, pool.GetConnectionIds(1, 2));
            Assert.Equal(new[] { "c1" }, pool.GetConnectionIds(1, 2, 3));
            Assert.Empty(pool.GetConnectionIds(1, 2, 9));

            var info = Assert.Single(pool.GetConnections(1));
            Assert.Equal(("c1", 1L, 2L, 3L), (info.ConnectionId, info.TenantId, info.UserId, info.AppId));
        }

        [Fact]
        public void Add_SameUserAcrossApps_GetByUserAggregates()
        {
            var pool = new ConnectionPool(new FakeHubContext());
            pool.Add("c1", 1, 2, 3);
            pool.Add("c2", 1, 2, 9);

            Assert.Equal(new[] { "c1", "c2" }, pool.GetConnectionIds(1, 2));
            Assert.Equal(new[] { "c1" }, pool.GetConnectionIds(1, 2, 3));
        }

        [Fact]
        public void Remove_RemovesConnection()
        {
            var pool = new ConnectionPool(new FakeHubContext());
            pool.Add("c1", 1, 2, 3);
            pool.Remove("c1");

            Assert.Empty(pool.GetConnectionIds(1, 2));
            Assert.Empty(pool.GetConnections(1));
        }

        [Fact]
        public void Remove_UnknownConnection_NoOp()
        {
            var pool = new ConnectionPool(new FakeHubContext());
            pool.Add("c1", 1, 2, 3);
            pool.Remove("unknown");

            Assert.Single(pool.GetConnectionIds(1, 2));
        }

        [Fact]
        public async Task ForceDisconnect_RemovesAndSends()
        {
            var hub = new FakeHubContext();
            var pool = new ConnectionPool(hub);
            pool.Add("c1", 1, 2, 3);

            await pool.ForceDisconnectAsync("c1");

            var call = Assert.Single(hub.Calls);
            Assert.Equal("Client", call.TargetKind);
            Assert.Equal("c1", call.TargetId);
            Assert.Equal(HertaLinkClientMethods.ForceDisconnect, call.Method);
            Assert.Empty(pool.GetConnectionIds(1, 2));
        }

        [Fact]
        public async Task ForceDisconnectUser_DisconnectsAllUserConnections()
        {
            var hub = new FakeHubContext();
            var pool = new ConnectionPool(hub);
            pool.Add("c1", 1, 2, 3);
            pool.Add("c2", 1, 2, 9);
            pool.Add("other", 1, 99, 3);

            await pool.ForceDisconnectUserAsync(1, 2);

            Assert.Equal(2, hub.Calls.Count);
            Assert.All(hub.Calls, c => Assert.Equal(HertaLinkClientMethods.ForceDisconnect, c.Method));
            Assert.Empty(pool.GetConnectionIds(1, 2));
        }

        [Fact]
        public async Task ForceDisconnectTenant_OnlyDisconnectsThatTenant()
        {
            var hub = new FakeHubContext();
            var pool = new ConnectionPool(hub);
            pool.Add("t1a", 1, 2, 3);
            pool.Add("t1b", 1, 3, 3);
            pool.Add("t2a", 2, 5, 3);

            await pool.ForceDisconnectTenantAsync(1);

            Assert.Equal(2, hub.Calls.Count);
            Assert.Single(pool.GetConnections(2));
        }

        [Fact]
        public void Clear_EmptiesEverything()
        {
            var pool = new ConnectionPool(new FakeHubContext());
            pool.Add("c1", 1, 2, 3);
            pool.Add("c2", 1, 3, 3);

            pool.Clear();

            Assert.Empty(pool.GetConnections(1));
            Assert.Empty(pool.GetConnectionIds(1, 2));
        }
    }
}
