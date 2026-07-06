
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Entity.Enums;
using Viv.Entity.Interface;

namespace Viv.Herta.Core.Entity.Message
{
    public class CommandMessage : IHertaChatMessage
    {
        public EmChatMessageType MessageType => EmChatMessageType.Command;

        /// <summary>
        /// 指令代号
        /// </summary>
        public int Command { get; set; }

        /// <summary>
        /// 扩展消息
        /// </summary>
        public Dictionary<string, object> Extend { get; set; } = [];
    }
}
