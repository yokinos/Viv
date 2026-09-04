using Viv.Contracts.Enums;
using Viv.Contracts.Exceptions;
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
    /// 6. Redis 不可用时当作 miss，回源数据库，不把缓存单点变成接口 502
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

        private static readonly T NullPlaceholder = new();

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
        /// 缓存不存在时自动回源数据库。Redis 故障时同样回源，不向外抛连接异常。
        /// </summary>
        public async Task<T?> GetCacheAsync(params object[] keys)
        {
            var bucket = new T();
            var cacheKey = bucket.GetCacheKey(keys);
            var lockKey = $"lock:{cacheKey}";
            var hasLock = false;

            try
            {
                try
                {
                    var cacheValue = await _redisService.GetAsync<T>(cacheKey).ConfigureAwait(false);
                    if (cacheValue != null)
                        return cacheValue;
                }
                catch (VivConnectionException ex) when (ex.ConnType == VivConnType.Redis)
                {
                    _logger.Error($"缓存不可用，回源数据库 Key:{cacheKey}", ex);
                    return await GetDbAsync(keys).ConfigureAwait(false);
                }

                for (int i = 0; i < MaxLockRetries; i++)
                {
                    hasLock = await _redisService.AcquireLockAsync(lockKey, LockExpireTime).ConfigureAwait(false);
                    if (hasLock)
                        break;

                    await Task.Delay(RetryDelayMs).ConfigureAwait(false);
                }

                if (hasLock)
                {
                    var cacheValue = await _redisService.GetAsync<T>(cacheKey).ConfigureAwait(false);
                    if (cacheValue != null)
                        return cacheValue;

                    var dbValue = await GetDbAsync(keys).ConfigureAwait(false);
                    try
                    {
                        if (dbValue != null)
                            await _redisService.AddAsync(cacheKey, dbValue, bucket.CacheTime).ConfigureAwait(false);
                        else
                            await _redisService.AddAsync(cacheKey, NullPlaceholder, NullValueCacheTime).ConfigureAwait(false);
                    }
                    catch (VivConnectionException ex) when (ex.ConnType == VivConnType.Redis)
                    {
                        _logger.Error($"回写缓存失败 Key:{cacheKey}", ex);
                    }

                    return dbValue;
                }

                await Task.Delay(RetryDelayMs).ConfigureAwait(false);
                try
                {
                    var cacheValue = await _redisService.GetAsync<T>(cacheKey).ConfigureAwait(false);
                    if (cacheValue != null)
                        return cacheValue;
                }
                catch (VivConnectionException ex) when (ex.ConnType == VivConnType.Redis)
                {
                    _logger.Error($"缓存不可用，回源数据库 Key:{cacheKey}", ex);
                }

                return await GetDbAsync(keys).ConfigureAwait(false);
            }
            catch (VivConnectionException ex) when (ex.ConnType == VivConnType.Redis)
            {
                _logger.Error($"缓存或锁不可用，回源数据库 Key:{cacheKey}", ex);
                return await GetDbAsync(keys).ConfigureAwait(false);
            }
            finally
            {
                if (hasLock)
                {
                    try
                    {
                        await _redisService.ReleaseLockAsync(lockKey).ConfigureAwait(false);
                    }
                    catch (Exception relEx)
                    {
                        _logger.Error($"释放缓存锁失败 Key:{lockKey}", relEx);
                    }
                }
            }
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
