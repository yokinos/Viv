using Viv.Delusion.Extension;
using Viv.Momo.Interface;

namespace Viv.Elysia.CacheBucket
{
    public class PairBucket<T1, T2> : ICacheBucket
    {
        public T1? First { get; set; }
        public T2? Second { get; set; }

        public TimeSpan CacheTime => CacheTimeProvider.GetRandomDays();

        public string GetCacheKey(params object[] keys)
        {
            if (keys.IsNullOrEmpty()) return string.Empty;
            return $"PairBucket_{typeof(T1).Name}_{typeof(T2).Name}_{string.Join("_", keys)}";
        }
    }
}
