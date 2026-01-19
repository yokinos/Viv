using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Redis
{
    /// <summary>
    /// Redis 部署模式
    /// </summary>
    public enum RedisMode
    {
        /// <summary>
        /// 单体部署模式
        /// </summary>
        Standalone = 0,

        /// <summary>
        /// 集群模式
        /// </summary>
        Cluster = 1,

        /// <summary>
        /// 哨兵模式
        /// </summary>
        Sentinel = 2
    }
}
