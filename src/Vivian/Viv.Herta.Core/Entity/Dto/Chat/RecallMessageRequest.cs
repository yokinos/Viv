using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Viv.Elysia.Request;
using Viv.Entity.Enums;

namespace Viv.Herta.Core.Entity.Dto.Chat
{
    public class RecallMessageRequest : VivApiRequest
    {
        [DisplayName("接收方类型")]
        public EmChatReceiverType ReceiverType { get; set; }

        /// <summary>单聊是对方用户Id，群聊是群Id</summary>
        [DisplayName("接收方")]
        public long TargetId { get; set; }

        [DisplayName("消息")]
        public long MessageId { get; set; }
    }
}
