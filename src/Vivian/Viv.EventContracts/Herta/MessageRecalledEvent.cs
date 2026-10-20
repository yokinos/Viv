using Viv.Entity.Enums;
using Viv.Nana;

namespace Viv.EventContracts.Herta
{
    public class MessageRecalledEvent : NanaEvent
    {
        public long TenantId { get; set; }

        public EmChatReceiverType ReceiverType { get; set; }

        /// <summary>单聊是对方用户Id，群聊是群Id</summary>
        public long TargetId { get; set; }

        public long MessageId { get; set; }

        public DateTimeOffset RecalledAt { get; set; }
    }
}
