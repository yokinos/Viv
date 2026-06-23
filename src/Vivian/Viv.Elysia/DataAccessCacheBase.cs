using System.Diagnostics.CodeAnalysis;
using Viv.Contracts.Interface;
using Viv.Elysia.Interface;
using Viv.Momo;
using Viv.Redis;

namespace Viv.Elysia
{
    /// <summary>
    /// 数据访问缓存基类 — Cache-Aside 模式
    /// 缓存优先 → 未命中则查库 → 回写缓存
    /// </summary>
    /// <typeparam name="T">缓存 Bucket 类型，必须实现 <see cref="ICacheBucket"/></typeparam>
    public abstract class DataAccessCacheBase<T> where T : ICacheBucket, new()
    {
        private readonly IRedisService _redisService;
        protected readonly IVivContext _context;
        protected readonly IMomoDbContext _dbContext;

        protected DataAccessCacheBase(IVivContext context, IMomoDbContext dbContext, IRedisService redisService)
        {
            _redisService = redisService;
            _context = context;
            _dbContext = dbContext;
        }

        /// <summary>
        /// 从数据库加载数据（子类实现）
        /// </summary>
        public abstract Task<T> GetDbAsync(params object[] keys);

        /// <summary>
        /// 从缓存获取数据（缓存穿透保护：空 Bucket 也会缓存）
        /// </summary>
        [return: MaybeNull]
        public async Task<T?> GetCacheAsync(params object[] keys)
        {
            var bucket = new T();
            var cacheKey = bucket.GetCacheKey(keys);
            var cacheValue = await _redisService.GetAsync<T>(cacheKey).ConfigureAwait(false);
            if (cacheValue != null)
                return cacheValue;

            var dbValue = await GetDbAsync(keys).ConfigureAwait(false);
            if (dbValue != null)
                await _redisService.AddAsync(cacheKey, dbValue, bucket.CacheTime);

            return dbValue;
        }

        /// <summary>
        /// 刷新缓存 — 删除缓存 Key，数据变更后调用保证一致
        /// </summary>
        public async Task<bool> RefreshAsync(params object[] keys)
        {
            var bucket = new T();
            var cacheKey = bucket.GetCacheKey(keys);
            return await _redisService.RemoveAsync(cacheKey).ConfigureAwait(false);
        }
    }
}
