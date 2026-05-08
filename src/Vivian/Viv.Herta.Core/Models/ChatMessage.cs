namespace Viv.Herta.Core.Models
{
    public class ChatMessage
    {
        public long MessageId { get; set; }
        public long FromUserId { get; set; }
        public long ToUserId { get; set; }
        public string Content { get; set; } = string.Empty;
        public MessageContentType ContentType { get; set; }
        public MediaFileInfo? MediaInfo { get; set; }
        public List<MessageSegment>? Segments { get; set; }
        public DateTimeOffset SentAt { get; set; }
    }
}
