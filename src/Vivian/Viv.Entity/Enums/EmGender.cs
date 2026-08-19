using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Viv.Entity.Enums
{
    public enum EmGender : byte
    {
        /// <summary>
        /// 未知
        /// </summary>
        [Description("未知")]
        Unknown = 0,

        /// <summary>
        /// 男
        /// </summary>
        [Description("男")]
        Male = 1,

        /// <summary>
        /// 女
        /// </summary>
        [Description("女")]
        Female = 2,

        /// <summary>
        /// 保密
        /// </summary>
        [Description("保密")]
        Secret = 3
    }
}
