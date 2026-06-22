using System;

namespace Viv.Momo.Enums
{
    /// <summary>
    /// 数据库类型
    /// </summary>
    public enum DatabaseSourceType
    {
        /// <summary>
        /// Microsoft SQL Server 数据库
        /// </summary>
        SqlServer = 0,

        /// <summary>
        /// PostgreSQL 数据库
        /// </summary>
        PostgreSQL = 1,

        /// <summary>
        /// SQLite 嵌入式数据库
        /// </summary>
        //Sqlite = 9
    }
}