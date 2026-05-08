namespace Viv.Herta.Core.Models
{
    public class MessageSegment
    {
        public MessageContentType Type { get; set; }
        public string Content { get; set; } = string.Empty;
        public MediaFileInfo? MediaInfo { get; set; }
    }
}
