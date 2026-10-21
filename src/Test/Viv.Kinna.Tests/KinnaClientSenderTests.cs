using Microsoft.AspNetCore.SignalR;
using Viv.Fakes;
using Viv.Kinna.Connections;

namespace Viv.Kinna.Tests
{
    /// <summary>只用来闭合 <see cref="KinnaClientSender{THub}"/> 的泛型；真实 Hub 类型留在各业务项目。</summary>
    public sealed class TestHub : Hub
    {
    }

    /// <summary>框架和 SignalR 之间就这一层，转发方向要盯住。</summary>
    public class KinnaClientSenderTests
    {
        [Fact]
        public async Task SendToConnectionAsync_发给单条连接()
        {
            var hub = new FakeHubContext<TestHub>();
            var sender = new KinnaClientSender<TestHub>(hub);

            await sender.SendToConnectionAsync("c1", "ForceDisconnect", []);

            var call = Assert.Single(hub.Calls);
            Assert.Equal("Client", call.TargetKind);
            Assert.Equal("c1", call.TargetId);
            Assert.Equal("ForceDisconnect", call.Method);
            Assert.Empty(call.Args);
        }

        [Fact]
        public async Task SendToConnectionsAsync_把载荷包成单元素参数()
        {
            var hub = new FakeHubContext<TestHub>();
            var sender = new KinnaClientSender<TestHub>(hub);
            var payload = new { Text = "hi" };

            await sender.SendToConnectionsAsync(["c1", "c2"], "ReceiveMessage", payload);

            var call = Assert.Single(hub.Calls);
            Assert.Equal("Clients", call.TargetKind);
            Assert.Equal("c1,c2", call.TargetId);
            Assert.Same(payload, Assert.Single(call.Args));
        }

        [Fact]
        public async Task SendToGroupAsync_发给组()
        {
            var hub = new FakeHubContext<TestHub>();
            var sender = new KinnaClientSender<TestHub>(hub);

            await sender.SendToGroupAsync("kinna:1:group:5", "ReceiveMessage", "payload");

            var call = Assert.Single(hub.Calls);
            Assert.Equal("Group", call.TargetKind);
            Assert.Equal("kinna:1:group:5", call.TargetId);
            Assert.Equal("payload", Assert.Single(call.Args));
        }

        [Fact]
        public async Task 加组与退组_走同一个组名()
        {
            var hub = new FakeHubContext<TestHub>();
            var sender = new KinnaClientSender<TestHub>(hub);
            var groups = (FakeGroupManager)hub.Groups;

            await sender.AddToGroupAsync("c1", "g");
            await sender.RemoveFromGroupAsync("c1", "g");

            Assert.Equal(("c1", "g"), Assert.Single(groups.Added));
            Assert.Equal(("c1", "g"), Assert.Single(groups.Removed));
        }
    }
}
