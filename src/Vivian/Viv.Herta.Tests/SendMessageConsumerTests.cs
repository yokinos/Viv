using Viv.Contracts.Models;
using Viv.Entity.Enums;
using Viv.EventContracts.Herta;
using Viv.Fakes;
using Viv.Herta.Core.Entity.Message;
using Viv.Herta.Link.Consumers;
using Viv.Herta.Link.Contracts;
using Viv.Herta.Link.Dispatch;
using Viv.Herta.Link.Hubs;
using Viv.Kinna.Contracts;
using Viv.Kinna.Dispatch;
using Viv.Kinna.Options;
using Viv.Nana;
using Viv.Nana.Options;

namespace Viv.Herta.Tests
{
    /// <summary>消费者 + 投递器一整条链：事件进来 → 身份校验 → 寻址 → 推给谁。</summary>
    public class SendMessageConsumerTests
    {
        private const long TenantId = 1;

        /// <summary>信封里的登录用户，也是唯一合法的发送者</summary>
        private const long FromUserId = 2;

        /// <summary>落库后的消息行Id（区别于信封的传输 Id）</summary>
        private const long PersistedMessageId = 456;

        private static readonly DateTimeOffset FixedSentAt = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        private static NanaEnvelope<SendMessageEvent> CreateEnvelope(
            EmChatReceiverType receiverType, long targetId, long fromUserId = FromUserId, bool noContext = false,
            long persistedMessageId = PersistedMessageId)
        {
            var evt = new SendMessageEvent(fromUserId, targetId, new TextMessage { Text = "hi" }, receiverType, EmChatMessageType.Text)
            {
                MessageId = persistedMessageId,
                SentAt = FixedSentAt
            };

            return new NanaEnvelope<SendMessageEvent>
            {
                MessageId = 123,
                Context = noContext ? null : new VivContextContent { AppId = 7, SubjectId = TenantId, UserId = FromUserId },
                Content = evt
            };
        }

        private static (SendMessageConsumer Consumer, RecordingLogger Logger) CreateConsumer(
            RecordingKinnaClientSender sender, StubKinnaConnectionPool pool)
        {
            var logger = new RecordingLogger();
            var session = new StubKinnaSession<ChatHub>(pool, sender, new KinnaKeys("herta"), new KinnaOptions());
            var dispatcher = new KinnaMessageDispatcher<ChatHub, SendMessageEvent>(
                session, new ChatMessagePusher(session), logger);

            var dependency = new VivConsumerDependency(
                logger,
                new TestContext(),
                new RecordingEventPublisher(),
                XUnitTestMagic.CreateOptions(new NanaOptions()),
                new RecordingLocalEventBus());

            return (new SendMessageConsumer(dependency, dispatcher), logger);
        }

        [Fact]
        public async Task ContentNull_ReturnsFailureWithoutSending()
        {
            var sender = new RecordingKinnaClientSender();
            var (consumer, _) = CreateConsumer(sender, new StubKinnaConnectionPool());

            var result = await consumer.ReceiveMessageAsync(new NanaEnvelope<SendMessageEvent> { MessageId = 1 });

            Assert.False(result.IsSuccess);
            Assert.False(result.IsRequeue);
            Assert.Contains("null", result.Message);
            Assert.Empty(sender.Calls);
        }

        [Fact]
        public async Task MissingContext_IsDroppedWithoutSending()
        {
            // 缺少可信上下文 = 不知道是谁、属于哪个租户，不能推
            var sender = new RecordingKinnaClientSender();
            var (consumer, _) = CreateConsumer(sender, new StubKinnaConnectionPool());

            var result = await consumer.ReceiveMessageAsync(
                CreateEnvelope(EmChatReceiverType.User, 5, noContext: true));

            Assert.False(result.IsSuccess);
            Assert.False(result.IsRequeue);
            Assert.Empty(sender.Calls);
        }

        [Fact]
        public async Task 代他人发送_按事件署名投递并留审计()
        {
            // 模拟发送、机器人代发：署名可以不是调用者，投递照常，只记一条审计
            var sender = new RecordingKinnaClientSender();
            var pool = new StubKinnaConnectionPool { ConnectionIds = ["c1"] };
            var (consumer, logger) = CreateConsumer(sender, pool);

            var result = await consumer.ReceiveMessageAsync(
                CreateEnvelope(EmChatReceiverType.User, 5, fromUserId: 999));

            Assert.True(result.IsSuccess);
            var msg = Assert.IsType<HertaLinkMessage>(Assert.Single(Assert.Single(sender.Calls).Args));
            Assert.Equal(999L, msg.FromUserId);
            Assert.Contains(logger.Infos, x => x.Contains("以他人名义"));
        }

        [Fact]
        public async Task 事件指定租户时覆盖信封租户()
        {
            // 模拟其他租户的用户发消息：租户取事件体，连接表和消息体都用它
            var sender = new RecordingKinnaClientSender();
            var pool = new StubKinnaConnectionPool { ConnectionIds = ["c1"] };
            var (consumer, _) = CreateConsumer(sender, pool);
            var envelope = CreateEnvelope(EmChatReceiverType.User, 5);
            envelope.Content!.TenantId = 99;

            var result = await consumer.ReceiveMessageAsync(envelope);

            Assert.True(result.IsSuccess);
            Assert.Equal(99L, pool.LastSubjectId);

            var msg = Assert.IsType<HertaLinkMessage>(Assert.Single(Assert.Single(sender.Calls).Args));
            Assert.Equal(99L, msg.TenantId);
        }

        [Fact]
        public async Task GroupReceiver_SendsToLocalGroup_WithFullPayload()
        {
            var sender = new RecordingKinnaClientSender();
            var (consumer, _) = CreateConsumer(sender, new StubKinnaConnectionPool());
            var envelope = CreateEnvelope(EmChatReceiverType.Group, 5);

            var result = await consumer.ReceiveMessageAsync(envelope);

            Assert.True(result.IsSuccess);
            var call = Assert.Single(sender.Calls);
            Assert.Equal("Group", call.TargetKind);
            Assert.Equal("herta:1:group:5", call.TargetId);
            Assert.Equal(KinnaClientMethods.ReceiveMessage, call.Method);

            var msg = Assert.IsType<HertaLinkMessage>(Assert.Single(call.Args));
            Assert.Equal(1, msg.V);
            // 推送用落库行 Id，客户端要拿它和拉取结果对上
            Assert.Equal(PersistedMessageId, msg.MessageId);
            Assert.Equal(TenantId, msg.TenantId);
            Assert.Equal(7L, msg.AppId);
            Assert.Equal(FromUserId, msg.FromUserId);
            // 群聊的接收方是群，ToUserId 留 0
            Assert.Equal(0L, msg.ToUserId);
            Assert.Equal(5L, msg.GroupId);
            Assert.Equal(EmChatReceiverType.Group, msg.ReceiverType);
            Assert.Equal(FixedSentAt, msg.SentAt);
            Assert.Equal("hi", Assert.IsType<TextMessage>(msg.Body).Text);
        }

        [Fact]
        public async Task MessageId未落库时回落信封Id()
        {
            // 升级窗口内的在途消息：落库前的旧事件没有 MessageId，只能退回信封 Id，至少不丢推
            var sender = new RecordingKinnaClientSender();
            var pool = new StubKinnaConnectionPool { ConnectionIds = ["c1"] };
            var (consumer, _) = CreateConsumer(sender, pool);

            var result = await consumer.ReceiveMessageAsync(
                CreateEnvelope(EmChatReceiverType.User, 5, persistedMessageId: 0));

            Assert.True(result.IsSuccess);
            var msg = Assert.IsType<HertaLinkMessage>(Assert.Single(Assert.Single(sender.Calls).Args));
            Assert.Equal(123L, msg.MessageId);
        }

        [Fact]
        public async Task UserReceiver_WithConnections_SendsToThoseClients()
        {
            var sender = new RecordingKinnaClientSender();
            var pool = new StubKinnaConnectionPool { ConnectionIds = new List<string> { "c1", "c2" } };
            var (consumer, _) = CreateConsumer(sender, pool);

            var result = await consumer.ReceiveMessageAsync(CreateEnvelope(EmChatReceiverType.User, 5));

            Assert.True(result.IsSuccess);
            Assert.Equal(TenantId, pool.LastSubjectId);
            Assert.Equal(5L, pool.LastUserId);

            var call = Assert.Single(sender.Calls);
            Assert.Equal("Clients", call.TargetKind);
            Assert.Equal("c1,c2", call.TargetId);

            var msg = Assert.IsType<HertaLinkMessage>(Assert.Single(call.Args));
            Assert.Equal(5L, msg.ToUserId);
            Assert.Null(msg.GroupId);
        }

        [Fact]
        public async Task UserReceiver_NoLocalConnections_SucceedsWithoutSending()
        {
            // 人不在本机是正常情况，不是失败
            var sender = new RecordingKinnaClientSender();
            var (consumer, _) = CreateConsumer(sender, new StubKinnaConnectionPool());

            var result = await consumer.ReceiveMessageAsync(CreateEnvelope(EmChatReceiverType.User, 5));

            Assert.True(result.IsSuccess);
            Assert.False(result.IsRequeue);
            Assert.Empty(sender.Calls);
        }
    }
}
