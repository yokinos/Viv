using System;

namespace Viv.Contracts.Exceptions
{
    /// <summary>
    /// 获取分布式锁失败异常
    /// </summary>
    /// <remarks>
    /// 当尝试获取分布式锁失败且未提供降级委托时抛出。
    /// 调用方可通过捕获此异常实现自定义降级逻辑。
    /// </remarks>
    public class DistributedLockException : Exception
    {
        /// <summary>
        /// 锁的 Key
        /// </summary>
        public string? LockKey { get; }

        /// <summary>
        /// 重试次数
        /// </summary>
        public int RetryCount { get; }

        public DistributedLockException() { }

        public DistributedLockException(string message) : base(message) { }

        public DistributedLockException(string message, Exception innerException) : base(message, innerException) { }

        public DistributedLockException(string lockKey, int retryCount) : base($"获取分布式锁失败，Key: {lockKey}，已重试 {retryCount} 次")
        {
            LockKey = lockKey;
            RetryCount = retryCount;
        }

        public DistributedLockException(string lockKey, int retryCount, Exception innerException) : base($"获取分布式锁失败，Key: {lockKey}，已重试 {retryCount} 次", innerException)
        {
            LockKey = lockKey;
            RetryCount = retryCount;
        }
    }
}