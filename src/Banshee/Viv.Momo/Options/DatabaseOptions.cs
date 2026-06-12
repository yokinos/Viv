using System;
using System.Collections.Generic;
using System.Text;
using Viv.Delusion.Magic;
using Viv.Momo.Enums;

namespace Viv.Momo.Options
{
    /// <summary>
    /// 数据库访问配置项
    /// 若需要支持动态访问数据库（比如根据不同的租户Id访问不同的数据库）
    /// </summary>
    public class DatabaseOptions
    {
        public DatabaseSouceType DatabaseSouce { get; set; } = DatabaseSouceType.PostgreSQL;

        /// <summary>
        /// 是否读写分离
        /// </summary>
        public bool IsReadWriteSplit { get; set; }

        /// <summary>
        /// 主库连接字符串（无论是否读写分离 这个都要有连接）
        /// </summary>
        public string MasterConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// 从库连接字符串（若没有读写分离 这个就不用设置）
        /// </summary>
        public string[] SlaveConnectionStrings { get; set; } = [];

        /// <summary>
        /// 超时时间（秒）
        /// </summary>
        public int Timeout { get; set; } = 30;

        /// <summary>
        /// 实体程序集名称(所有的实体都需要继承<see cref="Interface.IEntity"/>)
        /// </summary>
        public List<FilterTypeOptions> EntityTypeOptions { get; set; } = [];
    }
}
