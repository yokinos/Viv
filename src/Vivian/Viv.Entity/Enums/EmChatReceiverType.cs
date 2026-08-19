using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

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
        [Description("单聊")]
        User = 1,

        /// <summary>
        /// 群聊
        /// </summary>
        [Description("群聊")]
        Group = 2
    }
}
