using System;
using System.Reflection;
using System.Threading.Tasks;
using Viv.Contracts.Exceptions;
using Viv.Contracts.Interface;
using Viv.Delusion.Magic;
using Viv.Log;
using Viv.Redis;

namespace Viv.Engine
{
    /// <summary>
    /// 分布式锁业务处理者（带指数退避重试）
    /// </summary>
    public class DistributedLockAccessor : IDistributedLock
    {
        private readonly IRedisService _redisService;
        private readonly ILoggerContract _logger;

        public DistributedLockAccessor(IRedisService redisService, ILoggerContract logger)
        {
            _redisService = redisService;
            _logger = logger;
        }

        public async Task<bool> AcquireLockAsync(string lockKey, TimeSpan expire, string? lockHolderId = null, bool isReentrant = true)
        {
            return await _redisService.AcquireLockAsync(lockKey, expire, lockHolderId, isReentrant);
        }

        public async Task<bool> ReleaseLockAsync(string lockKey, string? lockHolderId = null, bool isReentrant = true)
        {
            return await _redisService.ReleaseLockAsync(lockKey, lockHolderId, isReentrant);
        }

        /// <summary>
        /// 尝试获取分布式锁（带指数退避重试，不执行业务逻辑）
        /// </summary>
        public async Task<bool> AcquireLockWithRetryAsync(
            string lockKey,
            TimeSpan expire,
            string? lockHolderId = null,
            bool isReentrant = true,
            int maxRetryCount = 5,
            int baseDelay = 200,
            int maxDelay = 5000,
            CancellationToken cancellationToken = default)
        {
            for (int attempt = 1; attempt <= maxRetryCount; attempt++)
            {
                if (cancellationToken.IsCancellationRequested)
                    return false;

                var acquired = await _redisService.AcquireLockAsync(lockKey, expire, lockHolderId, isReentrant);
                if (acquired)
                    return true;

                if (attempt < maxRetryCount)
                {
                    var delayMs = CalculateDelay(attempt, baseDelay, maxDelay);
                    _logger.Warning($"获取锁失败，第 {attempt} 次重试，Key: {lockKey}，等待 {delayMs}ms");
                    await Task.Delay(delayMs, cancellationToken);
                }
            }

            _logger.Warning($"获取锁失败，已达最大重试 {maxRetryCount} 次，Key: {lockKey}");
            return false;
        }

        /// <summary>
        /// 获取锁并执行业务委托（取锁成功执行业务，取锁失败执行降级）
        /// </summary>
        public async Task<T> AcquireLockWithExecuteAsync<T>(
            object key,
            TimeSpan expire,
            Func<Task<T>> executeMethod,
            Func<Task<T>>? fallbackMethod = null,
            string? lockHolderId = null,
            bool isReentrant = true,
            int maxRetryCount = 5,
            int baseDelay = 200,
            int maxDelay = 5000,
            CancellationToken cancellationToken = default)
        {
            var lockKey = GenerateLockKey(key);

            for (int attempt = 1; attempt <= maxRetryCount; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var acquired = await _redisService.AcquireLockAsync(lockKey, expire, lockHolderId, isReentrant);
                    if (acquired)
                    {
                        try
                        {
                            return await executeMethod();
                        }
                        catch (OperationCanceledException) { throw; }   // 业务被取消：透传，不包装
                        catch (Exception bizEx)
                        {
                            // 业务委托抛出的异常：不重试（避免双重执行）、不当取锁失败处理，
                            // 包装进 DistributedLockException.InnerException 上抛，保留原始异常。
                            throw new DistributedLockException(lockKey, attempt, bizEx);
                        }
                        finally
                        {
                            // 释放失败只记日志，不冒泡——否则会触发外层重试 → 重复执行业务
                            try { await _redisService.ReleaseLockAsync(lockKey, lockHolderId, isReentrant); }
                            catch (Exception relEx) { _logger.Error($"释放锁失败 Key: {lockKey}", relEx); }
                        }
                    }

                    if (attempt < maxRetryCount)
                    {
                        var delayMs = CalculateDelay(attempt, baseDelay, maxDelay);
                        _logger.Warning($"获取锁失败，第 {attempt} 次重试，Key: {lockKey}，等待 {delayMs}ms");
                        await Task.Delay(delayMs, cancellationToken);
                    }
                }
                catch (DistributedLockException) { throw; }        // 业务异常包装：直接透传，不重试
                catch (OperationCanceledException) { throw; }      // 取消：直接透传（含退避等待期取消）
                catch (Exception ex)
                {
                    // 只剩取锁/Redis 本身的异常才重试
                    _logger.Error($"获取锁异常，第 {attempt} 次尝试，Key: {lockKey}", ex);
                    if (attempt >= maxRetryCount)
                    {
                        return fallbackMethod is not null
                            ? await fallbackMethod()
                            : throw new DistributedLockException(lockKey, maxRetryCount, ex);
                    }
                    var delayMs = CalculateDelay(attempt, baseDelay, maxDelay);
                    await Task.Delay(delayMs, cancellationToken);
                }
            }

            _logger.Warning($"获取锁失败，已达最大重试 {maxRetryCount} 次，Key: {lockKey}");

            return fallbackMethod is not null
                ? await fallbackMethod()
                : throw new DistributedLockException(lockKey, maxRetryCount);
        }

        /// <summary>
        /// 计算指数退避延迟（带随机抖动防止惊群）
        /// </summary>
        private static int CalculateDelay(int attempt, int baseDelay, int maxDelay)
        {
            var delay = baseDelay * (int)Math.Pow(2, attempt - 1);
            delay = Math.Min(delay, maxDelay);
            var jitter = RandomMagic.Next(0, (int)(delay * 0.3));
            return delay + jitter;
        }

        private static string GenerateLockKey(object key)
        {
            if (key is string strKey)
                return strKey;

            return System.Text.Json.JsonSerializer.Serialize(key);
        }
    }
}