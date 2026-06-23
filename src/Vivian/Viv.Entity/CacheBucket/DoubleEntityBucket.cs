using System;
using System.Collections.Generic;
using System.Text;
using Viv.Delusion.Extension;
using Viv.Elysia;
using Viv.Elysia.Interface;

namespace Viv.Entity.CacheBucket
{
    /// <summary>
    /// 双实体桶，用于存储两个实体的缓存数据。
    /// </summary>
    /// <typeparam name="T1"></typeparam>
    /// <typeparam name="T2"></typeparam>
    public class DoubleEntityBucket<T1, T2> : ICacheBucket
    {
        public DoubleEntityBucket() { }

        public T1 Entity1 { get; set; }

        public T2 Entity2 { get; set; }

        public TimeSpan CacheTime => CacheTimeProvider.GetRandomDays();

        public string GetCacheKey(params object[] keys)
        {
            if (keys.IsNullOrEmpty()) return string.Empty;
            return $"DoubleEntityBucket_{typeof(T1).Name}_{typeof(T2).Name}_{string.Join("_", keys)}";
        }
    }
}
