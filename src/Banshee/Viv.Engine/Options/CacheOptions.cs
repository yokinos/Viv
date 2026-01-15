using System;
using System.Collections.Generic;
using System.Text;
using Viv.Cache.Redis;
using Viv.Engine.Enums;

namespace Viv.Engine.Options
{
    public record CacheOptions
    {
        /// <summary>
        /// 缓存类型
        /// </summary>
        public CacheProviderType CacheProviderType { get; set; }

        /// <summary>
        /// Redis配置
        /// </summary>
        public RedisOptions? RedisOptions { get; set; }
    }
}
