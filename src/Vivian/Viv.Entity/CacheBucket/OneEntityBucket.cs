using System;
using System.Collections.Generic;
using System.Text;
using Viv.Delusion.Extension;
using Viv.Elysia;
using Viv.Elysia.Interface;

namespace Viv.Entity.CacheBucket
{
    /// <summary>
    /// 一个实体的缓存
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class OneEntityBucket<T> : ICacheBucket
    {
        public OneEntityBucket() { }

        public T Entity { get; set; }

        public TimeSpan CacheTime => CacheTimeProvider.GetRandomDays();

        public string GetCacheKey(params object[] keys)
        {
            if (keys.IsNullOrEmpty()) return string.Empty;
            return $"OneEntityBucket_{typeof(T).Name}_{string.Join("_", keys)}";
        }
    }
}
