using System;
using System.Collections.Generic;
using System.Text;
using Viv.Elysia.Attributes;

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
        [EnumName("官方自营应用")]
        Viv = 0,

        /// <summary>
        /// 代理商定制应用（OEM）
        /// </summary>
        [EnumName("代理商定制应用")]
        OEM = 1,

        /// <summary>
        /// 第三方接入应用
        /// </summary>
        [EnumName("第三方接入应用")]
        Other = 3
    }
}