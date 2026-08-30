using Viv.Delusion.Extension;
using Viv.Momo.Interface;

namespace Viv.Elysia.CacheBucket
{
    public class EntityBucket<T> : ICacheBucket
    {
        public EntityBucket() { }

        public EntityBucket(T entity)
        {
            Entity = entity;
        }

        public T? Entity { get; set; }

        public TimeSpan CacheTime => CacheTimeProvider.GetRandomDays();

        public string GetCacheKey(params object[] keys)
        {
            if (keys.IsNullOrEmpty()) return string.Empty;
            return $"EntityBucket_{typeof(T).Name}_{string.Join("_", keys)}";
        }
    }
}
