using System.Diagnostics.CodeAnalysis;
using Viv.Contracts.Interface;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Interface;
using Viv.Redis;

namespace Viv.Momo.Base
{
    /// <summary>
    /// 数据访问缓存基类 — Cache-Aside 模式
    /// 缓存优先 → 未命中则查库 → 回写缓存
    /// 
    /// 特性：
    /// 1. 缓存命中直接返回
    /// 2. 缓存未命中时使用分布式锁防止击穿
    /// 3. 双重检查，避免重复查库
    /// 4. 支持空值缓存，防止缓存穿透
    /// 5. 未拿到锁时短暂退避后重试缓存，避免直接打爆数据库
    /// </summary>
    /// <typeparam name="T">缓存 Bucket 类型，必须实现 <see cref="ICacheBucket"/></typeparam>
    public abstract class DataAccessCacheBase<T> where T : ICacheBucket, new()
    {
        private readonly IRedisService _redisService;
        protected readonly IVivContext _context;
        protected readonly IMomoDbContext _dbContext;
        protected readonly ILoggerContract _logger;

        private const int MaxLockRetries = 3;
        private const int RetryDelayMs = 20;
        private static readonly TimeSpan NullValueCacheTime = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan LockExpireTime = TimeSpan.FromSeconds(5);

        protected DataAccessCacheBase(IVivContext context, IMomoDbContext dbContext, IRedisService redisService, ILoggerContract logger)
        {
            _redisService = redisService;
            _context = context;
            _dbContext = dbContext;
            _logger = logger;
        }

        /// <summary>
        /// 从数据库加载数据（子类实现）
        /// 返回 null 表示数据库中不存在对应记录
        /// </summary>
        public abstract Task<T?> GetDbAsync(params object[] keys);

        /// <summary>
        /// 从缓存获取数据
        /// 缓存不存在时自动回源数据库
        /// </summary>
        public async Task<T?> GetCacheAsync(params object[] keys)
        {
            var bucket = new T();
            var cacheKey = bucket.GetCacheKey(keys);

            // 先查缓存
            var cacheValue = await _redisService.GetAsync<T>(cacheKey).ConfigureAwait(false);
            if (cacheValue != null)
                return cacheValue;

            // 尝试获取分布式锁，避免缓存击穿
            var lockKey = $"lock:{cacheKey}";
            bool hasLock = false;

            for (int i = 0; i < MaxLockRetries; i++)
            {
                hasLock = await _redisService.AcquireLockAsync(lockKey, LockExpireTime).ConfigureAwait(false);
                if (hasLock)
                    break;

                await Task.Delay(RetryDelayMs).ConfigureAwait(false);
            }

            // 拿到锁：二次检查缓存，再查数据库
            if (hasLock)
            {
                try
                {
                    cacheValue = await _redisService.GetAsync<T>(cacheKey).ConfigureAwait(false);
                    if (cacheValue != null)
                        return cacheValue;

                    var dbValue = await GetDbAsync(keys).ConfigureAwait(false);

                    if (dbValue != null)
                    {
                        await _redisService.AddAsync(cacheKey, dbValue, bucket.CacheTime).ConfigureAwait(false);
                    }
                    else
                    {
                        // 缓存空对象，防止穿透
                        await _redisService.AddAsync(cacheKey, new T(), NullValueCacheTime).ConfigureAwait(false);
                    }

                    return dbValue;
                }
                finally
                {
                    await _redisService.ReleaseLockAsync(lockKey).ConfigureAwait(false);
                }
            }

            // 没拿到锁：短暂退避后再试一次缓存
            await Task.Delay(RetryDelayMs).ConfigureAwait(false);
            cacheValue = await _redisService.GetAsync<T>(cacheKey).ConfigureAwait(false);
            if (cacheValue != null)
                return cacheValue;

            // 兜底：继续查库，保证业务可用
            // 注意：这里不写缓存，避免无锁并发写入
            return await GetDbAsync(keys).ConfigureAwait(false);
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
