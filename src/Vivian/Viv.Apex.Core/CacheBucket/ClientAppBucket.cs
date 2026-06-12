using System;
using System.Collections.Generic;
using System.Text;
using Viv.Elysia;
using Viv.Elysia.Interface;
using Viv.Entity.Database.Apex;

namespace Viv.Apex.Core.CacheBucket
{
    /// <summary>
    /// 客户端应用缓存Bucket
    /// 缓存维度：Id
    /// 缓存策略：随机天数
    /// </summary>
    public class ClientAppBucket : ICacheBucket
    {
        /// <summary>
        /// 客户端应用缓存
        /// </summary>
        public AtClientApp? ClientApp { get; set; }

        public TimeSpan CacheTime => CacheTimeProvider.GetRandomDays();

        public string GetCacheKey(params object[] keys)
        {
            return $"ClientAppBucket_{keys[0]}";
        }
    }
}
