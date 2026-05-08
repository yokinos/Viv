using Viv.Herta.Core.Models;
using Viv.Nana.Core;

namespace Viv.Herta.Core.Events
{
    public class SendMessageEvent : VivEvent
    {
        public long MessageId { get; set; }
        public long FromUserId { get; set; }
        public long ToUserId { get; set; }
        public string Content { get; set; } = string.Empty;
        public MessageContentType ContentType { get; set; }
        public MediaFileInfo? MediaInfo { get; set; }
        public List<MessageSegment>? Segments { get; set; }
    }
}
