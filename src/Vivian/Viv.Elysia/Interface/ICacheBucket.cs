using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Elysia.Interface
{
    public interface ICacheBucket
    {
        /// <summary>
        /// 获取缓存时间
        /// </summary>
        TimeSpan CacheTime { get; }

        /// <summary>
        /// 获取缓存键值
        /// </summary>
        /// <returns></returns>
        string GetCacheKey(params object[] keys);
    }
}
