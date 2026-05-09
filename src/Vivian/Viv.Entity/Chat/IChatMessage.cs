using System;
using System.Collections.Generic;
using System.Text;
using Viv.Entity.Enums;

namespace Viv.Entity.Chat
{
    public interface IChatMessage
    {
        /// <summary>
        /// 消息类型
        /// </summary>
        public EmChatMessageType MessageType { get; }
    }
}
