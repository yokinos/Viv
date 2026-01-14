using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Vva.Enum
{
    public enum DateFormat
    {
        /// <summary>
        /// 短日期（仅年月日，无分隔符）：yyyyMMdd
        /// </summary>
        ShortDate,

        /// <summary>
        /// 标准日期（年月日，横线分隔）：yyyy-MM-dd
        /// </summary>
        Date,

        /// <summary>
        /// 长日期（年月日时分秒，横线+冒号分隔）：yyyy-MM-dd HH:mm:ss
        /// </summary>
        LongDate,

        /// <summary>
        /// 紧凑长日期（年月日时分秒，无分隔符）：yyyyMMddHHmmss
        /// </summary>
        CompactLongDate,

        /// <summary>
        /// 时间（仅时分秒，无分隔符）：HHmmss
        /// </summary>
        Time,

        /// <summary>
        /// 标准时间（仅时分秒，冒号分隔）：HH:mm:ss
        /// </summary>
        StandardTime
    }
}
