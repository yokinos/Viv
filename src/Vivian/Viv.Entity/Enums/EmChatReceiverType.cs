using System;
using System.Collections.Generic;
using System.Text;
using Viv.Elysia.Attributes;

namespace Viv.Entity.Enums
{
    /// <summary>
    /// 消息接收方类型
    /// </summary>
    public enum EmChatReceiverType : byte
    {
        /// <summary>
        /// 单人 / 私聊
        /// </summary>
        [EnumName("单聊")]
        User = 1,

        /// <summary>
        /// 群聊
        /// </summary>
        [EnumName("群聊")]
        Group = 2
    }
}
