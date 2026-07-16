using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Viv.Elysia.Request;
using Viv.Entity.Enums;

namespace Viv.Herta.Core.Entity.Dto.Chat
{
    public class SendMessageRequest : ApiRequestBase
    {
        /// <summary>
        /// [可选]发送方的Id 不选默认为当前登录人的Id
        /// </summary>
        [DisplayName("发送方")]
        public long FromUserId { get; set; }

        /// <summary>
        /// [必传]接收方的Id
        /// </summary>
        [Required]
        [DisplayName("接收方")]
        public long TargetId { get; set; }

        /// <summary>
        /// [必传]接收方的类型
        /// </summary>
        [Required]
        [DisplayName("接收方类型")]
        public EmChatReceiverType ReceiverType { get; set; }

        /// <summary>
        /// [必传]消息类型
        /// </summary>
        [Required]
        [DisplayName("消息类型")]
        public EmChatMessageType MessageType { get; set; }

        /// <summary>
        /// [必传]消息[Json]
        /// </summary>
        [Required]
        [DisplayName("消息内容")]
        public string Message { get; private set; }
    }
}
