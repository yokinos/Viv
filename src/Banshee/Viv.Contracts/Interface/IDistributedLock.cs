using System;
using System.Threading.Tasks;

namespace Viv.Contracts.Interface
{
    /// <summary>
    /// 分布式锁
    /// </summary>
    public interface IDistributedLock
    {
        /// <summary>
        /// 通过分布式锁执行任务
        /// </summary>
        /// <param name="key">匿名对象或者字符串</param>
        /// <param name="expire">过期时间</param>
        /// <param name="lockHolderId">锁持有者Id 不传会自动用当前上下文的TrackId</param>
        /// <param name="isReentrant"></param>
        /// <returns></returns>
        Task<T> AcquireLockAsync<T>(object key, TimeSpan expire, string? lockHolderId = null, bool isReentrant = true);
    }
}