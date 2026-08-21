using System.Collections.Generic;
using System.Threading.Tasks;
using Viv.Contracts.Models;
using Viv.Entity.Enums;
using Viv.EventContracts.Herta;
using Viv.Herta.Core.Entity.Message;
using Viv.Herta.Link.Consumers;
using Viv.Herta.Link.Hubs;
using Viv.Nana;

namespace Viv.Herta.Tests
{
    public class SendMessageConsumerTests
    {
        private static NanaEnvelope<SendMessageEvent> CreateEnvelope(EmChatReceiverType receiverType, long targetId)
        {
            var evt = new SendMessageEvent(2, targetId, new TextMessage { Text = "hi" }, receiverType, EmChatMessageType.Text);
            return new NanaEnvelope<SendMessageEvent>
            {
                MessageId = 123,
                Context = new VivContextContent { AppId = 7, SubjectId = 1, UserId = 2 },
                Content = evt
            };
        }

        private static SendMessageConsumer CreateConsumer(FakeHubContext hub, StubConnectionPool pool)
            => new(new FakeLogger(),new FakeContext(), hub, pool);

        [Fact]
        public async Task ContentNull_ReturnsFailureWithoutSending()
        {
            var hub = new FakeHubContext();
            var consumer = CreateConsumer(hub, new StubConnectionPool());

            var result = await consumer.ReceiveMessageAsync(new NanaEnvelope<SendMessageEvent> { MessageId = 1 });

            Assert.False(result.IsSuccess);
            Assert.False(result.IsRequeue);
            Assert.Contains("null", result.Message);
            Assert.Empty(hub.Calls);
        }

        [Fact]
        public async Task GroupReceiver_SendsToGroup_WithHydratedChatMessage()
        {
            var hub = new FakeHubContext();
            var consumer = CreateConsumer(hub, new StubConnectionPool());

            var result = await consumer.ReceiveMessageAsync(CreateEnvelope(EmChatReceiverType.Group, 5));

            Assert.True(result.IsSuccess);
            var call = Assert.Single(hub.Calls);
            Assert.Equal("Group", call.TargetKind);
            Assert.Equal(HertaLinkGroups.GetGroupName(1, 5), call.TargetId);
            Assert.Equal(HertaLinkClientMethods.ReceiveMessage, call.Method);

            var msg = Assert.IsType<HertaChatMessage>(Assert.Single(call.Args));
            Assert.Equal(123L, msg.Id);
            Assert.Equal(7L, msg.AppId);
            Assert.Equal(2L, msg.FromUserId);
            Assert.Equal(5L, msg.ToUserId);
            Assert.Equal("hi", Assert.IsType<TextMessage>(msg.Body).Text);
        }

        [Fact]
        public async Task UserReceiver_WithConnections_SendsToClients()
        {
            var hub = new FakeHubContext();
            var pool = new StubConnectionPool { ConnectionIds = new List<string> { "c1", "c2" } };
            var consumer = CreateConsumer(hub, pool);

            var result = await consumer.ReceiveMessageAsync(CreateEnvelope(EmChatReceiverType.User, 5));

            Assert.True(result.IsSuccess);
            Assert.Equal(1L, pool.LastTenantId);
            Assert.Equal(5L, pool.LastUserId);
            var call = Assert.Single(hub.Calls);
            Assert.Equal("Clients", call.TargetKind);
            Assert.Equal("c1,c2", call.TargetId);
        }

        [Fact]
        public async Task UserReceiver_NoConnections_DoesNotSend()
        {
            var hub = new FakeHubContext();
            var consumer = CreateConsumer(hub, new StubConnectionPool());

            var result = await consumer.ReceiveMessageAsync(CreateEnvelope(EmChatReceiverType.User, 5));

            Assert.True(result.IsSuccess);
            Assert.Empty(hub.Calls);
        }
    }
}
