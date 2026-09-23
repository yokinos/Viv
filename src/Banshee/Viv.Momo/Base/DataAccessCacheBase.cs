using Viv.Contracts;
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
        protected readonly IRedisService _redisService;
        protected readonly IDistributedLock _distributedLock;
        protected readonly IVivContext _context;
        protected readonly IMomoDbContext _dbContext;
        protected readonly ILoggerContract _logger;
        private const int MaxLockRetries = 3;
        private const int RetryDelayMs = 20;
        private static readonly TimeSpan NullValueCacheTime = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan LockExpireTime = TimeSpan.FromSeconds(5);

        private static readonly T NullPlaceholder = new();

        protected DataAccessCacheBase(IVivContext context, IMomoDbContext dbContext, IRedisService redisService, IDistributedLock distributedLock, ILoggerContract logger)
        {
            _redisService = redisService;
            _distributedLock = distributedLock;
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
            var lockKey = LockKeyMagic.Join(LockKeyMagic.BusinessPrefix, cacheKey);
            var hasLock = false;

            try
            {
                var cacheValue = await _redisService.GetAsync<T>(cacheKey).ConfigureAwait(false);
                RedisMetrics.RecordCache(cacheValue != null);

                if (cacheValue != null)
                    return cacheValue;

                hasLock = await _distributedLock.AcquireLockWithRetryAsync(
                    lockKey,
                    LockExpireTime,
                    maxRetryCount: MaxLockRetries,
                    baseDelay: RetryDelayMs,
                    maxDelay: RetryDelayMs).ConfigureAwait(false);

                if (hasLock)
                {
                    // 再次检查下是否有其他线程已写入
                    cacheValue = await _redisService.GetAsync<T>(cacheKey).ConfigureAwait(false);
                    RedisMetrics.RecordCache(cacheValue != null);

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

                // 如果没抢到锁 略微等待后再次从缓存尝试获取数据
                await Task.Delay(RetryDelayMs).ConfigureAwait(false);
                cacheValue = await _redisService.GetAsync<T>(cacheKey).ConfigureAwait(false);
                RedisMetrics.RecordCache(cacheValue != null);

                if (cacheValue != null)
                    return cacheValue;

                // 行吧 从数据库读
                return await GetDbAsync(keys).ConfigureAwait(false);
            }
            catch (VivConnectionException ex) when (ex.ConnType == VivConnType.Redis)
            {
                _logger.Error($"缓存或锁不可用，回源数据库 Key:{cacheKey}", ex);
                // 这两处回源是「Redis 故障」与「数据库压力翻倍」之间唯一的连接点：
                // 一次故障会让所有缓存读压到主库上，而日志是一次一条的，只有速率看得出来。
                RedisMetrics.RecordFallback("cache");
                return await GetDbAsync(keys).ConfigureAwait(false);
            }
            catch (DistributedLockException ex) when (ex.InnerException is VivConnectionException { ConnType: VivConnType.Redis })
            {
                _logger.Error($"锁不可用，回源数据库 Key:{cacheKey}", ex);
                RedisMetrics.RecordFallback("lock");
                return await GetDbAsync(keys).ConfigureAwait(false);
            }
            finally
            {
                if (hasLock)
                {
                    try
                    {
                        await _distributedLock.ReleaseLockAsync(lockKey).ConfigureAwait(false);
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
