using System.Threading.Tasks;
using Viv.Delusion.Extension;
using Viv.Entity.Enums;
using Viv.EventContracts.Herta;
using Viv.Fakes;
using Viv.Herta.Core.Entity.Dto.Chat;
using Viv.Herta.Core.Entity.Message;
using Viv.Herta.Core.Service;

namespace Viv.Herta.Tests
{
    public class ChatServiceTests
    {
        private static SendMessageRequest CreateTextRequest() => new()
        {
            FromUserId = 2,
            TargetId = 5,
            ReceiverType = EmChatReceiverType.User,
            MessageType = EmChatMessageType.Text,
            Message = new TextMessage { Text = "hi" }.ToJson()
        };

        [Fact]
        public async Task SendMessage_Text_ReturnsSuccessAndEnqueuesOutbox()
        {
            var outbox = new RecordingOutbox();
            var service = new ChatService(outbox);

            var result = await service.SendMessageAsync(CreateTextRequest());

            Assert.True(result.Code >= 200);
            var evt = Assert.IsType<SendMessageEvent>(Assert.Single(outbox.Enqueued));
            Assert.Equal(2L, evt.FromUserId);
            Assert.Equal(5L, evt.TargetId);
        }

        [Fact]
        public async Task SendMessage_UnsupportedType_ReturnsFailedAndDoesNotEnqueue()
        {
            var outbox = new RecordingOutbox();
            var service = new ChatService(outbox);
            var request = CreateTextRequest();
            request.MessageType = unchecked((EmChatMessageType)999);
            request.Message = "{}";

            var result = await service.SendMessageAsync(request);

            Assert.False(result.Code >= 200);
            Assert.Empty(outbox.Enqueued);
        }
    }
}
