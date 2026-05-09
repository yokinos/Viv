
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Entity.Chat;
using Viv.Entity.Enums;

namespace Viv.Entity.Chat.Message
{
    public class CommandMessage : IChatMessage
    {
        public EmChatMessageType MessageType => EmChatMessageType.Command;

        /// <summary>
        /// 指令代号
        /// </summary>
        public int Command { get; set; }
    }
}
