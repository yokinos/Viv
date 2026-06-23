using System;
using System.Collections.Generic;
using System.Text;
using Viv.Delusion.Extension;
using Viv.Elysia;
using Viv.Elysia.Interface;

namespace Viv.Entity.CacheBucket
{
    public class OneEntityWithListBucket<T1, T2> : ICacheBucket
    {
        public OneEntityWithListBucket() { }

        public T1 Entity { get; set; }

        public List<T2> Entities { get; set; }

        public TimeSpan CacheTime => CacheTimeProvider.GetRandomDays();

        public string GetCacheKey(params object[] keys)
        {
            if (keys.IsNullOrEmpty()) return string.Empty;
            return $"DoubleEntityBucket_{typeof(T1).Name}_{typeof(T2).Name}_{string.Join("_", keys)}";
        }
    }
}
