using System;
using System.Collections.Generic;
using System.Text;
using Viv.Cache.Redis;
using Viv.Engine.Enums;

#nullable disable
namespace Viv.Engine.Options
{
    /// <summary>
    /// 缓存配置
    /// </summary>
    public record VivCacheOptions
    {
        /// <summary>
        /// 缓存类型
        /// </summary>
        public CacheProviderType CacheProviderType { get; set; }

        /// <summary>
        /// Redis配置
        /// </summary>
        public RedisOptions RedisOptions { get; set; }
    }
}
