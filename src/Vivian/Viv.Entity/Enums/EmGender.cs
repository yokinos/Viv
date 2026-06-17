using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using Viv.Elysia.Attributes;

namespace Viv.Entity.Enums
{
    public enum EmGender : byte
    {
        /// <summary>
        /// 未知
        /// </summary>
        [EnumName("未知")]
        Unknown = 0,

        /// <summary>
        /// 男
        /// </summary>
        [EnumName("男")]
        Male = 1,

        /// <summary>
        /// 女
        /// </summary>
        [EnumName("女")]
        Female = 2,

        /// <summary>
        /// 保密
        /// </summary>
        [EnumName("保密")]
        Secret = 3
    }
}
