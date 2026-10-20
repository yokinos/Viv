using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Viv.Elysia.Request;
using Viv.Entity.Enums;

namespace Viv.Herta.Core.Entity.Dto.Chat
{
    /// <summary>
    /// 增量拉取消息。与 <see cref="SendMessageRequest"/> 同一套寻址参数（对端类型 + 对端Id），
    /// 客户端不需要先拿 sessionId。
    /// </summary>
    public class PullMessagesRequest : VivApiRequest
    {
        [Required]
        [DisplayName("接收方")]
        public long TargetId { get; set; }

        [Required]
        [DisplayName("接收方类型")]
        public EmChatReceiverType ReceiverType { get; set; }

        /// <summary>只取这个消息Id之后的消息（向后增量）；0 = 不设下界</summary>
        [DisplayName("游标")]
        public long AfterMessageId { get; set; }

        /// <summary>
        /// 只取这个消息Id之前的消息（向前翻历史）；0 = 不设上界。
        /// 与 <see cref="AfterMessageId"/> 都为 0 时返回最新一批。
        /// </summary>
        [DisplayName("历史游标")]
        public long BeforeMessageId { get; set; }

        [DisplayName("条数")]
        public int Limit { get; set; } = 50;

        public override string Validate()
        {
            var error = base.Validate();
            if (!string.IsNullOrEmpty(error)) return error;

            // 单次拉取上限，防止一次把整个会话读进内存
            if (Limit is <= 0 or > 200) return "Limit 必须在 1..200 之间";

            if (AfterMessageId > 0 && BeforeMessageId > 0)
                return "AfterMessageId 与 BeforeMessageId 只能给一个";

            return string.Empty;
        }
    }
}
