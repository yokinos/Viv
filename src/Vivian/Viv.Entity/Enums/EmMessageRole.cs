using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Entity.Enums
{
    /// <summary>
    /// 消息角色。与 <c>Microsoft.Extensions.AI.ChatRole</c> 一一对应，
    /// 落库用 int（与其它 Em* 一致），对外接口仍以字符串暴露。
    /// </summary>
    public enum EmMessageRole
    {
        /// <summary>
        /// 用户
        /// </summary>
        User = 1,

        /// <summary>
        /// 助手
        /// </summary>
        Assistant = 2,

        /// <summary>
        /// 工具结果
        /// </summary>
        Tool = 3,

        /// <summary>
        /// 系统提示
        /// </summary>
        System = 4
    }
}
