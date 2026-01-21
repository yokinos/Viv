using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Redis
{
    public enum DbSelectorType
    {
        /// <summary>
        /// 不设置key分配 所有的key都会落到RedisOptions的DefaultDatabase
        /// </summary>
        None = 0,

        /// <summary>
        /// 根据缓存key的hash自动分配
        /// </summary>
        KeyHash = 1,

        /// <summary>
        /// SaaS系统专属 根据租户的Id Hash后自动分配
        /// </summary>
        TenantIdHash = 2,
    }
}
