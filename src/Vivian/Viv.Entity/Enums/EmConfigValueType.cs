using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Entity.Enums
{
    /// <summary>
    /// 配置值类型提示（OtConfig.ValueType）
    /// </summary>
    public enum EmConfigValueType
    {
        /// <summary>
        /// 字符串
        /// </summary>
        Text = 1,

        /// <summary>
        /// 整数
        /// </summary>
        Integer = 2,

        /// <summary>
        /// 小数
        /// </summary>
        Decimal = 3,

        /// <summary>
        /// 布尔
        /// </summary>
        Boolean = 4,

        /// <summary>
        /// JSON
        /// </summary>
        Json = 5
    }
}
