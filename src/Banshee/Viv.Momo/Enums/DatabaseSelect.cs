using System;

namespace Viv.Momo.Enums
{
    /// <summary>
    /// 数据库来源
    /// </summary>
    public enum DatabaseSelect
    {
        /// <summary>
        /// [已支持] PostgreSQL 数据库
        /// </summary>
        PostgreSQL = 0,

        /// <summary>
        /// [已支持] Microsoft SQL Server 数据库
        /// </summary>
        MsSql = 1,

        /// <summary>
        /// [已支持] Oracle 数据库
        /// </summary>
        Oracle = 2,

        /// <summary>
        /// [未支持] MySQL/MariaDB 数据库
        /// </summary>
        MySql = 3,

        /// <summary>
        /// [已支持] SQLite 嵌入式数据库
        /// </summary>
        Sqlite = 9
    }
}