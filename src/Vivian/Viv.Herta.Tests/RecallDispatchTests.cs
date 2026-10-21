using Viv.Entity.Enums;
using Viv.EventContracts.Herta;
using Viv.Fakes;
using Viv.Herta.Link.Contracts;
using Viv.Herta.Link.Dispatch;
using Viv.Herta.Link.Hubs;
using Viv.Kinna.Contracts;
using Viv.Kinna.Dispatch;
using Viv.Kinna.Options;
using Viv.Nana;

namespace Viv.Herta.Tests
{
    public class RecallDispatchTests
    {
        private const long TenantId = 1;
        private const long TargetId = 5;
        private const long MessageId = 77;

        private static KinnaMessageDispatcher<ChatHub, MessageRecalledEvent> Create(
            RecordingKinnaClientSender sender,
            StubKinnaConnectionPool pool)
        {
            var session = new StubKinnaSession<ChatHub>(pool, sender, new KinnaKeys("herta"), new KinnaOptions());
            return new KinnaMessageDispatcher<ChatHub, MessageRecalledEvent>(
                session, new ChatRecallPusher(session), new RecordingLogger());
        }

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
            var sender = new RecordingKinnaClientSender();
            var pool = new StubKinnaConnectionPool { ConnectionIds = ["c1"] };

            var outcome = await Create(sender, pool).DispatchAsync(
                CreateEvent(EmChatReceiverType.User), new KinnaPushContext(7, TenantId, 2, null));

            Assert.Equal(KinnaPushOutcome.Success, outcome);
            var call = Assert.Single(sender.Calls);
            Assert.Equal(KinnaClientMethods.MessageRecalled, call.Method);
            Assert.Equal("c1", call.TargetId);

            var payload = Assert.IsType<HertaLinkRecall>(Assert.Single(call.Args));
            Assert.Equal(MessageId, payload.MessageId);
        }

        [Fact]
        public async Task 群聊撤回_推给群组()
        {
            var sender = new RecordingKinnaClientSender();

            var outcome = await Create(sender, new StubKinnaConnectionPool()).DispatchAsync(
                CreateEvent(EmChatReceiverType.Group), null);

            Assert.Equal(KinnaPushOutcome.Success, outcome);
            var call = Assert.Single(sender.Calls);
            Assert.Equal("Group", call.TargetKind);
            Assert.Equal("herta:1:group:5", call.TargetId);
        }

        [Fact]
        public async Task 缺租户_计划为空_丢弃()
        {
            var sender = new RecordingKinnaClientSender();
            var evt = CreateEvent(EmChatReceiverType.User);
            evt.TenantId = 0;

            var outcome = await Create(sender, new StubKinnaConnectionPool()).DispatchAsync(evt, null);

            Assert.Equal(KinnaPushOutcome.Dropped, outcome);
            Assert.Empty(sender.Calls);
        }
    }
}
