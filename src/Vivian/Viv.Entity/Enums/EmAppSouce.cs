using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Entity.Enums
{
    /// <summary>
    /// 应用来源类型
    /// </summary>
    public enum EmAppSouce
    {
        /// <summary>
        /// 官方自营应用（Viv 框架原生）
        /// </summary>
        Viv = 0,

        /// <summary>
        /// 代理商定制应用（OEM）
        /// </summary>
        OEM = 1,

        /// <summary>
        /// 第三方接入应用
        /// </summary>
        Other = 3
    }
}