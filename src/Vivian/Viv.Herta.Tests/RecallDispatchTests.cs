using Viv.Entity.Enums;
using Viv.EventContracts.Herta;
using Viv.Fakes;
using Viv.Herta.Link.Contracts;
using Viv.Herta.Link.Dispatch;
using Viv.Herta.Link.Hubs;
using Viv.Nana;

namespace Viv.Herta.Tests
{
    public class RecallDispatchTests
    {
        private const long TenantId = 1;
        private const long TargetId = 5;
        private const long MessageId = 77;

        private static SignalRMessageDispatcher Create(FakeHubContext hub, StubConnectionPool pool)
            => new(hub, pool, new FanoutPlanner(), new RecordingLogger());

        private static MessageRecalledEvent CreateEvent(EmChatReceiverType receiverType) => new()
        {
            TenantId = TenantId,
            ReceiverType = receiverType,
            TargetId = TargetId,
            MessageId = MessageId,
            RecalledAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)
        };

        [Fact]
        public async Task 单聊撤回_推给对端的本机连接()
        {
            var hub = new FakeHubContext();
            var pool = new StubConnectionPool { ConnectionIds = ["c1"] };

            var result = await Create(hub, pool).DispatchRecallAsync(
                new NanaEnvelope<MessageRecalledEvent>(), CreateEvent(EmChatReceiverType.User));

            Assert.True(result.IsSuccess);
            var call = Assert.Single(hub.Calls);
            Assert.Equal(HertaLinkClientMethods.MessageRecalled, call.Method);
            Assert.Equal("c1", call.TargetId);

            var payload = Assert.IsType<HertaLinkRecall>(Assert.Single(call.Args));
            Assert.Equal(MessageId, payload.MessageId);
        }

        [Fact]
        public async Task 群聊撤回_推给群组()
        {
            var hub = new FakeHubContext();

            var result = await Create(hub, new StubConnectionPool()).DispatchRecallAsync(
                new NanaEnvelope<MessageRecalledEvent>(), CreateEvent(EmChatReceiverType.Group));

            Assert.True(result.IsSuccess);
            var call = Assert.Single(hub.Calls);
            Assert.Equal("Group", call.TargetKind);
            Assert.Equal(HertaLinkKeys.GetGroupName(TenantId, TargetId), call.TargetId);
        }
    }
}
