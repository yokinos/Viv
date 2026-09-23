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
        public DatabaseSourceType DatabaseSource { get; set; } = DatabaseSourceType.PostgreSQL;

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

        /// <summary>
        /// 启动时按实体同步表结构（建缺失的表、加缺失的列）。默认关。
        /// 只加不改不删，对已有库安全。开发期打开，生产期交给迁移脚本。
        /// </summary>
        public bool SyncTableOnStartup { get; set; }

        /// <summary>
        /// 慢查询阈值（毫秒）。超过则记一条 Warning 并计入 viv.momo.query.slow，0 或负数 = 关。
        ///
        /// 作用于全部库访问，手写的大 SQL 也在内。判的是单条命令：批量写与 ExecuteSqlList
        /// 一条一条算，只有分页的 count 与 list 合成一个样本。
        /// </summary>
        public int SlowQueryThresholdMs { get; set; } = 1000;
    }
}
