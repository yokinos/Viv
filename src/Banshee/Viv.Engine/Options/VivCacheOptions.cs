using System;
using System.Collections.Generic;
using System.Text;
using Viv.Engine.Enums;
using Viv.Redis;

#nullable disable
namespace Viv.Engine.Options
{
    /// <summary>
    /// 缓存配置
    /// </summary>
    public record VivCacheOptions
    {
        /// <summary>
        /// 分布式缓存类型
        /// </summary>
        public DistributedCacheType CacheProviderType { get; set; }

        /// <summary>
        /// Redis配置
        /// </summary>
        public RedisOptions RedisOptions { get; set; }

        /// <summary>
        /// 是否启用内存缓存
        /// <see cref="Contracts.Interface.IMemoryCacheService"/>
        /// </summary>
        public bool IsEnableMemoryCache { get; set; } = true;
    }
}
