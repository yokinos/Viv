using System;
using System.Collections.Generic;
using System.Text;
using Viv.Entity.Enums;

namespace Viv.Entity.Interface
{
    public interface IChatMessage
    {
        /// <summary>
        /// 消息类型
        /// </summary>
        public EmChatMessageType MessageType { get; }
    }
}
