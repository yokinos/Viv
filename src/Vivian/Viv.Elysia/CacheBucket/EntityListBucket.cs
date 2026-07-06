using Viv.Delusion.Extension;
using Viv.Momo.Interface;

namespace Viv.Elysia.CacheBucket
{
    public class EntityListBucket<T1, T2> : ICacheBucket
    {
        public T1? Entity { get; set; }
        public List<T2>? Entities { get; set; }

        public TimeSpan CacheTime => CacheTimeProvider.GetRandomDays();

        public string GetCacheKey(params object[] keys)
        {
            if (keys.IsNullOrEmpty()) return string.Empty;
            return $"EntityListBucket_{typeof(T1).Name}_{typeof(T2).Name}_{string.Join("_", keys)}";
        }
    }
}
