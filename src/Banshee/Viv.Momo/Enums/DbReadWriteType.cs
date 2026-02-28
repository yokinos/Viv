using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Momo.Enums
{
    /// <summary>
    /// 数据库读写操作类型
    /// </summary>
    public enum DbReadWriteType
    {
        /// <summary>
        /// 写操作（主库）
        /// </summary>
        Write = 0,

        /// <summary>
        /// 读操作（从库）
        /// </summary>
        Read = 1
    }
}