using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Entity.Enums
{
    /// <summary>
    /// 消息内容类型：正文、工具调用、工具结果
    /// </summary>
    public enum EmMessageContentType
    {
        /// <summary>
        /// 纯文本正文
        /// </summary>
        Text = 1,

        /// <summary>
        /// 模型发起的工具调用
        /// </summary>
        FunctionCall = 2,

        /// <summary>
        /// 工具返回结果
        /// </summary>
        FunctionResult = 3
    }
}
