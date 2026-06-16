using System;
using System.Collections.Generic;
using System.Text;
using Viv.Elysia.Attributes;

namespace Viv.Entity.Enums
{
    public enum EmStatus
    {
        /// <summary>
        /// 正常
        /// </summary>
        [EnumName("正常")]
        Normal = 0,

        /// <summary>
        /// 禁用
        /// </summary>
        [EnumName("禁用")]
        Disabled = 1
    }
}
