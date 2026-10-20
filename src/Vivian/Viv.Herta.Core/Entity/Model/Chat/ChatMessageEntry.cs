using Viv.Entity.Enums;

namespace Viv.Herta.Core.Entity.Model.Chat
{
    /// <summary>
    /// 消息的统一表示。单聊与群聊分表存，读写两侧都走这个模型。
    /// </summary>
    public sealed record ChatMessageEntry
    {
        public long Id { get; init; }

        public long TenantId { get; init; }

        public long SessionId { get; init; }

        public EmChatReceiverType ReceiverType { get; init; }

        public long FromUserId { get; init; }

        /// <summary>单聊的接收人</summary>
        public long? ToUserId { get; init; }

        /// <summary>群聊的群Id</summary>
        public long? GroupId { get; init; }

        public EmChatMessageType MessageType { get; init; }

        /// <summary>消息体 JSON，按 <see cref="MessageType"/> 解释</summary>
        public string Body { get; init; } = string.Empty;

        public long? ReplyMsgId { get; init; }

        public DateTime SentAt { get; init; }

        public bool IsRecall { get; init; }

        public DateTime? RecallAt { get; init; }
    }
}
