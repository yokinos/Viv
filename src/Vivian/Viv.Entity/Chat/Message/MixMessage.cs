using System;
using System.Collections.Generic;
using System.Text;
using Viv.Entity.Chat;
using Viv.Entity.Enums;

namespace Viv.Entity.Chat.Message
{
    public class MixMessage : IChatMessage
    {
        public EmChatMessageType MessageType => EmChatMessageType.Mix;

        /// <summary>
        /// 混合消息列表
        /// </summary>
        public List<IChatMessage> MixList { get; set; }

        /// <summary>
        /// 扩展消息
        /// </summary>
        public Dictionary<string, object> Extend { get; set; } = [];
    }
}
