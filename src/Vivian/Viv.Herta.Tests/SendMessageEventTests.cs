using System.Text.Json;
using Viv.Entity.Enums;
using Viv.EventContracts.Herta;
using Viv.Herta.Core.Entity.Message;
using Viv.Herta.Core.Magic;

namespace Viv.Herta.Tests
{
    /// <summary>
    /// 事件要过发件箱的 JSON 存盘，序列化配置是 System.Text.Json 默认 —— 契约里
    /// 任何非 public setter 都会在反序列化时被跳过，消费端拿到空值。这条钉住它。
    /// </summary>
    public class SendMessageEventTests
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

        [Fact]
        public void 消息体经过JSON往返不丢()
        {
            var original = new SendMessageEvent(1, 5, new TextMessage { Text = "hi" }, EmChatReceiverType.User, EmChatMessageType.Text);

            var json = JsonSerializer.Serialize(original, Options);
            var restored = JsonSerializer.Deserialize<SendMessageEvent>(json, Options)!;

            Assert.Equal(original.Message, restored.Message);
            Assert.Contains("hi", restored.Message);

            // 解析出来还是同一条消息
            var body = HertaMagic.GetChatMessage(restored.MessageType, restored.Message);
            Assert.Equal("hi", Assert.IsType<TextMessage>(body).Text);
        }
    }
}
