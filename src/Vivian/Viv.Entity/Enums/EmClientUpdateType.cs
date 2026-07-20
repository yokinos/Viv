using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Entity.Enums
{
    /// <summary>
    /// 客户端版本更新类型
    /// </summary>
    public enum EmClientUpdateType
    {
        /// <summary>
        /// 可选更新，弹窗可关闭
        /// </summary>
        Optional = 0,

        /// <summary>
        /// 强制更新，不更新无法进入系统
        /// </summary>
        Force = 1
    }
}
