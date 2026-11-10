using Viv.Entity.Enums;
using Viv.Nana;

namespace Viv.EventContracts.Herta
{
    public class MessageRecalledEvent : NanaEvent
    {
        /// <summary>消息所属聊天域。与 SendMessageEvent 同义：信封 SubjectId 在聊天链路上承载 RealmId。</summary>
        public long RealmId { get; set; }

        public EmChatReceiverType ReceiverType { get; set; }

        /// <summary>单聊是对方用户Id，群聊是群Id</summary>
        public long TargetId { get; set; }

        public long MessageId { get; set; }

        public DateTimeOffset RecalledAt { get; set; }
    }
}
