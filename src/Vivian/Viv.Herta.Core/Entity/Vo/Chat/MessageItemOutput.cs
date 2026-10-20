using Viv.Entity.Enums;

namespace Viv.Herta.Core.Entity.Vo.Chat
{
    /// <summary>
    /// 拉取返回的单条消息。与实时推送的 <c>HertaLinkMessage</c> 用同一个 Id，客户端据此去重。
    /// </summary>
    public class MessageItemOutput
    {
        public long Id { get; set; }

        public long SessionId { get; set; }

        public long FromUserId { get; set; }

        /// <summary>单聊的接收人；群聊为 null</summary>
        public long? ToUserId { get; set; }

        /// <summary>群聊的群Id；单聊为 null</summary>
        public long? GroupId { get; set; }

        public EmChatMessageType MessageType { get; set; }

        /// <summary>消息体 JSON，按 <see cref="MessageType"/> 解释</summary>
        public string Body { get; set; } = string.Empty;

        public long? ReplyMsgId { get; set; }

        /// <summary>与实时推送的 SentAt 同一形状（带时区偏移），客户端两条通道可以混用</summary>
        public DateTimeOffset SentAt { get; set; }

        /// <summary>已撤回。客户端应显示占位而不是原文</summary>
        public bool IsRecall { get; set; }

        public DateTimeOffset? RecallAt { get; set; }
    }
}
