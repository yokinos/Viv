using System;
using System.Collections.Generic;
using System.Text;
using Viv.Entity.Enums;
using Viv.Entity.Interface;

namespace Viv.Herta.Core.Entity.Message
{
    public class TextMessage : IHertaChatMessage
    {
        public EmChatMessageType MessageType => EmChatMessageType.Text;

        /// <summary>
        /// 文本消息内容
        /// </summary>
        public string? Text { get; set; }

        /// <summary>
        /// 扩展消息
        /// </summary>
        public Dictionary<string, object> Extend { get; set; } = [];
    }
}
