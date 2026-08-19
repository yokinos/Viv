using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Viv.Entity.Enums
{
    /// <summary>
    /// 应用来源类型
    /// </summary>
    public enum EmAppSouce : byte
    {
        /// <summary>
        /// 官方自营应用
        /// </summary>
        [Description("官方自营应用")]
        Viv = 0,

        /// <summary>
        /// 代理商定制应用（OEM）
        /// </summary>
        [Description("代理商定制应用")]
        OEM = 1,

        /// <summary>
        /// 第三方接入应用
        /// </summary>
        [Description("第三方接入应用")]
        Other = 3
    }
}