#nullable enable
using System;
using System.Threading.Tasks;

namespace Viv.Redis
{
    /// <summary>
    /// 定义分布式锁的标准契约，便于扩展不同实现
    /// </summary>
    public interface IDistributedLock
    {
        /// <summary>
        /// 【同步】获取可重入分布式锁
        /// </summary>
        /// <param name="lockKey">锁的唯一标识（如：stock_lock_1001）</param>
        /// <param name="lockHolderId">锁持有者唯一标识</param>
        /// <param name="expireTime">锁过期时间（必须>0，防止死锁）</param>
        /// <param name="enableReentrant">是否启用重入，默认true</param>
        /// <returns>true=加锁/重入成功，false=加锁失败</returns>
        bool AcquireLock(string lockKey, string lockHolderId, TimeSpan expireTime, bool enableReentrant = true);

        /// <summary>
        /// 【同步】释放可重入分布式锁
        /// </summary>
        /// <param name="lockKey">锁的唯一标识</param>
        /// <param name="lockHolderId">锁持有者唯一标识（必须与加锁时一致）</param>
        /// <param name="enableReentrant">是否启用重入，需和加锁时一致</param>
        /// <returns>true=释放/重入次数减1成功，false=锁不属于当前持有者/锁不存在</returns>
        bool ReleaseLock(string lockKey, string lockHolderId, bool enableReentrant = true);

        /// <summary>
        /// 【异步】获取可重入分布式锁
        /// </summary>
        /// <param name="lockKey">锁的唯一标识</param>
        /// <param name="lockHolderId">锁持有者唯一标识</param>
        /// <param name="expireTime">锁过期时间</param>
        /// <param name="enableReentrant">是否启用重入，默认true</param>
        /// <returns>true=加锁/重入成功，false=加锁失败</returns>
        Task<bool> AcquireLockAsync(string lockKey, string lockHolderId, TimeSpan expireTime, bool enableReentrant = true);

        /// <summary>
        /// 【异步】释放可重入分布式锁
        /// </summary>
        /// <param name="lockKey">锁的唯一标识</param>
        /// <param name="lockHolderId">锁持有者唯一标识</param>
        /// <param name="enableReentrant">是否启用重入，需和加锁时一致</param>
        /// <returns>true=释放/重入次数减1成功，false=锁不属于当前持有者/锁不存在</returns>
        Task<bool> ReleaseLockAsync(string lockKey, string lockHolderId, bool enableReentrant = true);
    }
}