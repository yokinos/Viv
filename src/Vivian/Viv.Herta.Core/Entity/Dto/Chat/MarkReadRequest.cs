using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Viv.Elysia.Request;
using Viv.Entity.Enums;

namespace Viv.Herta.Core.Entity.Dto.Chat
{
    public class MarkReadRequest : VivApiRequest
    {
        [Required]
        [DisplayName("接收方")]
        public long TargetId { get; set; }

        [Required]
        [DisplayName("接收方类型")]
        public EmChatReceiverType ReceiverType { get; set; }

        /// <summary>已读到的消息Id（含）</summary>
        [Required]
        [DisplayName("已读消息")]
        public long LastReadMessageId { get; set; }

        public override string Validate()
        {
            var error = base.Validate();
            if (!string.IsNullOrEmpty(error)) return error;

            if (LastReadMessageId <= 0) return "LastReadMessageId 必须大于 0";

            return string.Empty;
        }
    }
}
