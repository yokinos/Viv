using System;
using System.Collections.Generic;
using System.Text;
using Viv.Momo.Enums;

namespace Viv.Momo.Options
{
    /// <summary>
    /// 数据库访问配置项
    /// 若需要支持动态访问数据库（比如根据不同的租户Id访问不同的数据库）
    /// </summary>
    public class DatabaseOptions
    {
        public DatabaseSouceType DatabaseSouce { get; set; }

        /// <summary>
        /// 是否读写分离
        /// </summary>
        public bool IsReadWriteSplit { get; set; }

        /// <summary>
        /// 连接字符串
        /// 若是读写分离，则为读连接字符串和写连接字符串的数组 [0]为写连接字符串，[1]为读连接字符串
        /// </summary>
        public string[] ConnectionStrings { get; set; } = [];

        /// <summary>
        /// 是否需要动态切换数据库
        /// 如果需要动态切换 需要实现 <see cref="Interface.IConnectionSelect"/>
        /// </summary>
        public bool IsNeedDanamicChangeDatabase { get; set; } = false;
    }
}
