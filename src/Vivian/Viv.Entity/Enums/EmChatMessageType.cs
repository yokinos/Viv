using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Entity.Enums
{
    public enum EmChatMessageType
    {
        /// <summary>
        /// 文本消息
        /// </summary>
        Text = 0,

        /// <summary>
        /// 媒体文件消息
        /// </summary>
        MediaFile = 1,

        /// <summary>
        /// 混合消息
        /// </summary>
        Mix = 2,

        /// <summary>
        /// 指令消息
        /// </summary>
        Command = 9,
    }
}
