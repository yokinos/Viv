using System;
using System.Collections.Generic;
using System.Text;
using Viv.Elysia.Attributes;

namespace Viv.Entity.Enums
{
    public enum EmChatMessageType : byte
    {
        /// <summary>
        /// 文本消息
        /// </summary>
        [EnumName("文本消息")]
        Text = 0,

        /// <summary>
        /// 媒体文件消息
        /// </summary>
        [EnumName("媒体文件消息")]
        MediaFile = 1,

        /// <summary>
        /// 混合消息
        /// </summary>
        [EnumName("混合消息")]
        Mix = 2,

        /// <summary>
        /// 指令消息
        /// </summary>
        [EnumName("指令消息")]
        Command = 9,
    }
}
