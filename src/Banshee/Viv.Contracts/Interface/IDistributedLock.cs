using System;
using System.Threading.Tasks;

namespace Viv.Contracts.Interface
{
    /// <summary>
    /// 分布式锁服务接口
    /// </summary>
    /// <remarks>
    /// 提供基于 Redis 的分布式锁能力，支持：
    /// <list type="bullet">
    /// <item><description>可重入锁</description></item>
    /// <item><description>指数退避重试（含随机抖动，防止惊群）</description></item>
    /// <item><description>自动获取锁并执行业务逻辑</description></item>
    /// <item><description>获取锁失败时的降级机制</description></item>
    /// </list>
    /// </remarks>
    public interface IDistributedLock
    {
        /// <summary>
        /// 获取分布式锁（单次尝试，不重试）
        /// </summary>
        /// <param name="lockKey">锁的唯一标识</param>
        /// <param name="expire">锁过期时间（必须大于 TimeSpan.Zero，防止死锁）</param>
        /// <param name="lockHolderId">锁持有者唯一标识（不传则自动从上下文获取 TraceId）</param>
        /// <param name="isReentrant">是否启用重入，默认 true</param>
        /// <returns><c>true</c> = 加锁/重入成功；<c>false</c> = 锁已被其他持有者占用</returns>
        Task<bool> AcquireLockAsync(string lockKey, TimeSpan expire, string? lockHolderId = null, bool isReentrant = true);

        /// <summary>
        /// 查询锁当前是否被持有（取锁失败后用于区分「真竞争」与「服务瞬时不稳/故障」）
        /// </summary>
        /// <param name="lockKey">锁的唯一标识</param>
        /// <returns>
        /// <c>true</c> = 锁确实被其他持有者占用（真竞争）；
        /// <c>false</c> = 锁未被持有（说明刚才取锁失败是瞬时不稳/命令异常）
        /// </returns>
        /// <exception cref="DistributedLockException">Redis 不可用、无法确认锁状态时抛出</exception>
        Task<bool> IsLockHeldAsync(string lockKey);

        /// <summary>
        /// 释放分布式锁
        /// </summary>
        /// <param name="lockKey">锁的唯一标识</param>
        /// <param name="lockHolderId">锁持有者唯一标识（不传则自动从上下文获取 TraceId）</param>
        /// <param name="isReentrant">是否启用重入，需与加锁时保持一致</param>
        /// <returns><c>true</c> = 释放/重入次数减1成功；<c>false</c> = 锁不属于当前持有者 或 锁不存在</returns>
        Task<bool> ReleaseLockAsync(string lockKey, string? lockHolderId = null, bool isReentrant = true);

        /// <summary>
        /// 尝试获取分布式锁（带指数退避重试，不执行业务逻辑）
        /// </summary>
        /// <param name="lockKey">锁的唯一标识</param>
        /// <param name="expire">锁过期时间</param>
        /// <param name="lockHolderId">锁持有者唯一标识（不传则自动从上下文获取 TraceId）</param>
        /// <param name="isReentrant">是否启用重入，默认 true</param>
        /// <param name="maxRetryCount">最大重试次数，默认 5 次</param>
        /// <param name="baseDelay">退避基础延迟（毫秒），默认 200ms</param>
        /// <param name="maxDelay">最大延迟上限（毫秒），默认 5000ms</param>
        /// <param name="cancellationToken"></param>
        /// <returns><c>true</c> = 成功获取锁；<c>false</c> = 获取失败，已达最大重试次数</returns>
        /// <remarks>
        /// 与 <see cref="AcquireLockWithExecuteAsync{T}(object, TimeSpan, Func{Task{T}}, Func{Task{T}}?, string?, bool, int, int, int)"/> 的区别：
        /// 本方法仅负责获取锁，不执行业务逻辑，锁需要调用方手动通过 <see cref="ReleaseLockAsync"/> 释放。
        /// </remarks>
        Task<bool> AcquireLockWithRetryAsync(string lockKey, TimeSpan expire, string? lockHolderId = null, bool isReentrant = true, int maxRetryCount = 5, int baseDelay = 200, int maxDelay = 5000, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取锁并执行业务委托（取锁成功执行业务，取锁失败执行降级）
        /// </summary>
        /// <typeparam name="T">返回值类型</typeparam>
        /// <param name="key">锁标识（字符串或对象，对象会自动序列化为 JSON 作为 Key）</param>
        /// <param name="expire">锁过期时间</param>
        /// <param name="executeMethod">业务委托（取锁成功时执行）</param>
        /// <param name="fallbackMethod">降级委托（取锁失败时执行）。为 null 时取锁失败抛出 <see cref="DistributedLockException"/></param>
        /// <param name="lockHolderId">锁持有者唯一标识（不传则自动从上下文获取 TraceId）</param>
        /// <param name="isReentrant">是否启用重入，默认 true</param>
        /// <param name="maxRetryCount">最大重试次数，默认 5 次</param>
        /// <param name="baseDelay">退避基础延迟（毫秒），默认 200ms</param>
        /// <param name="maxDelay">最大延迟上限（毫秒），默认 5000ms</param>
        /// <param name="cancellationToken"></param>
        /// <returns>业务委托或降级委托的执行结果</returns>
        /// <exception cref="DistributedLockException">
        /// 获取锁失败且 <paramref name="fallbackMethod"/> 为 null 时抛出
        /// </exception>
        /// <remarks>
        /// 重试策略：指数退避 + 随机抖动（30%），避免惊群效应
        /// </remarks>
        Task<T> AcquireLockWithExecuteAsync<T>(object key, TimeSpan expire, Func<Task<T>> executeMethod, Func<Task<T>>? fallbackMethod = null, string? lockHolderId = null, bool isReentrant = true, int maxRetryCount = 5, int baseDelay = 200, int maxDelay = 5000, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取锁并执行业务委托
        /// </summary>
        /// <typeparam name="T">返回值类型</typeparam>
        /// <param name="key">锁标识（字符串或对象，对象会自动序列化为 JSON 作为 Key）</param>
        /// <param name="expire">锁过期时间</param>
        /// <param name="executeMethod">业务委托（取锁成功时执行）</param>
        /// <returns></returns>
        Task<T> AcquireLockAsync<T>(object key, TimeSpan expire, Func<Task<T>> executeMethod, string? lockHolderId = null, bool isReentrant = true, CancellationToken cancellationToken = default) => AcquireLockWithExecuteAsync(key, expire, executeMethod, null, lockHolderId, isReentrant, 5, 200, 5000, cancellationToken);
    }
}
