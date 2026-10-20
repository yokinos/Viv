namespace Viv.Herta.Core.Entity.Dto.Chat
{
    /// <summary>发送成功后的回执，客户端拿 MessageId 做幂等与游标</summary>
    public class SendMessageOutput
    {
        public long MessageId { get; set; }

        public long SessionId { get; set; }

        public DateTime SentAt { get; set; }
    }
}
