using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Viv.Entity.Enums
{
    public enum EmStatus : byte
    {
        /// <summary>
        /// 正常
        /// </summary>
        [Description("正常")]
        Normal = 0,

        /// <summary>
        /// 禁用
        /// </summary>
        [Description("禁用")]
        Disabled = 1
    }
}
