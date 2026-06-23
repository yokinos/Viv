using System;
using System.Collections.Generic;
using System.Text;
using Viv.Delusion.Extension;
using Viv.Elysia;
using Viv.Elysia.Interface;

namespace Viv.Entity.CacheBucket
{
    public class OneListBucket<T> : ICacheBucket
    {
        public OneListBucket() { }

        public List<T> Entities { get; set; }

        public TimeSpan CacheTime => CacheTimeProvider.GetRandomDays();

        public string GetCacheKey(params object[] keys)
        {
            if (keys.IsNullOrEmpty()) return string.Empty;
            return $"OneListBucket_{typeof(T).Name}_{string.Join("_", keys)}";
        }
    }
}
