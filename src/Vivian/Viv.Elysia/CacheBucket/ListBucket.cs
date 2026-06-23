using Viv.Delusion.Extension;
using Viv.Elysia.Interface;

namespace Viv.Elysia.CacheBucket
{
    public class ListBucket<T> : ICacheBucket
    {
        public List<T>? Entities { get; set; }

        public TimeSpan CacheTime => CacheTimeProvider.GetRandomDays();

        public string GetCacheKey(params object[] keys)
        {
            if (keys.IsNullOrEmpty()) return string.Empty;
            return $"ListBucket_{typeof(T).Name}_{string.Join("_", keys)}";
        }
    }
}
