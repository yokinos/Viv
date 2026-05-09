using System;
using System.Collections.Generic;
using System.Text;
using Viv.Entity.Chat;
using Viv.Entity.Enums;

namespace Viv.Entity.Chat.Message
{
    public class TextMessage : IChatMessage
    {
        public EmChatMessageType MessageType => EmChatMessageType.Text;

        /// <summary>
        /// 文本消息内容
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// 扩展消息
        /// </summary>
        public Dictionary<string, object> Extend { get; set; } = [];
    }
}
