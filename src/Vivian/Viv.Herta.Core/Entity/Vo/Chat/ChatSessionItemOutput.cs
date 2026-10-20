using Viv.Entity.Enums;

namespace Viv.Herta.Core.Entity.Vo.Chat
{
    /// <summary>
    /// 会话列表项。单聊不带名字/头像：本域没有用户资料表，客户端按 TargetId 自己取。
    /// </summary>
    public class ChatSessionItemOutput
    {
        public EmChatReceiverType ReceiverType { get; set; }

        /// <summary>单聊是对方用户Id，群聊是群Id</summary>
        public long TargetId { get; set; }

        /// <summary>群名；单聊为 null</summary>
        public string? Name { get; set; }

        /// <summary>群头像；单聊为 null</summary>
        public string? AvatarUrl { get; set; }

        public DateTimeOffset? LastMessageAt { get; set; }

        public bool IsTop { get; set; }

        public bool NoDisturb { get; set; }

        /// <summary>已读水位</summary>
        public long LastReadMsgId { get; set; }

        /// <summary>未读条数。群聊不含自己发的</summary>
        public int UnreadCount { get; set; }
    }
}
