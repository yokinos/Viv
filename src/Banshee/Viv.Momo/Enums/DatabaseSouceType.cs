using System;

namespace Viv.Momo.Enums
{
    /// <summary>
    /// 数据库类型
    /// </summary>
    public enum DatabaseSouceType
    {
        /// <summary>
        /// PostgreSQL 数据库
        /// </summary>
        PostgreSQL = 0,

        /// <summary>
        /// Microsoft SQL Server 数据库
        /// </summary>
        SqlServer = 1,

        /// <summary>
        /// SQLite 嵌入式数据库
        /// </summary>
        //Sqlite = 9
    }
}