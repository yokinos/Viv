using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Viv.Contracts.Interface;
using Viv.Elysia.Interface;
using Viv.Momo;
using Viv.Redis;

namespace Viv.Entity.Database
{
    /// <summary>
    /// 数据访问缓存基类
    /// 提供统一的 缓存读取、数据库查询、缓存刷新 能力
    /// </summary>
    /// <typeparam name="T">缓存Bucket类型</typeparam>
    public abstract class DataAccessCacheBase<T> where T : ICacheBucket, new()
    {
        private readonly IRedisService _redisService;
        protected readonly IVivContext _vivContext;
        protected readonly IMomoDbContext _dbContext;

        protected DataAccessCacheBase(IVivContext context, IMomoDbContext dbContext, IRedisService redisService)
        {
            _redisService = redisService;
            _vivContext = context;
            _dbContext = dbContext;
        }

        /// <summary>
        /// 从数据库加载数据（子类实现）
        /// </summary>
        /// <param name="keys">缓存键参数</param>
        /// <returns>Bucket实体</returns>
        public abstract Task<T> GetDbAsync(params object[] keys);

        /// <summary>
        /// 从缓存获取数据（缓存穿透：空Bucket也会缓存）
        /// 缓存优先 → 未命中则查库 → 回写缓存
        /// </summary>
        /// <param name="keys">缓存键参数</param>
        /// <returns>Bucket实体</returns>
        [return: MaybeNull]
        public async Task<T?> GetCacheAsync(params object[] keys)
        {
            var bucket = new T();
            var cacheKey = bucket.GetCacheKey(keys);
            var cacheValue = await _redisService.GetAsync<T>(cacheKey).ConfigureAwait(false);
            if (cacheValue != null)
            {
                return cacheValue;
            }

            var dbValue = await GetDbAsync(keys).ConfigureAwait(false);
            if (dbValue != null)
            {
                // 缓存穿透防护 由子类实现 若需要防止穿透 则返回返回一个空的bucket
                await _redisService.AddAsync(cacheKey, dbValue, bucket.CacheTime);
            }

            return dbValue;
        }

        /// <summary>
        /// 刷新缓存（删除缓存Key）
        /// 新增/修改/删除数据时调用，保证数据一致
        /// </summary>
        /// <param name="keys">缓存键参数</param>
        /// <returns>是否删除成功</returns>
        public async Task<bool> RefreshAsync(params object[] keys)
        {
            var bucket = new T();
            var cacheKey = bucket.GetCacheKey(keys);
            return await _redisService.RemoveAsync(cacheKey).ConfigureAwait(false);
        }
    }
}