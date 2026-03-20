using System.Threading;
using Viv.Vva.Extension;

namespace Viv.Redis
{
    /// <summary>
    /// 锁持有者上下文
    /// </summary>
    public static class LockHolderContext
    {
        private static readonly AsyncLocal<string> _holderId = new();

        /// <summary>
        /// 获取当前异步流中的锁持有者 ID，若不存在则自动生成一个唯一 ID。
        /// </summary>
        public static string CurrentHolderId
        {
            get
            {
                if (_holderId.Value.IsNullOrEmpty())
                    _holderId.Value = GenerateHolderId();

                return _holderId.Value;
            }
        }

        public static void ResetHolderId() => _holderId.Value = GenerateHolderId();

        /// <summary>
        /// 显式设置持有者 ID（可用于测试或特殊情况）
        /// </summary>
        public static void SetHolderId(string holderId) => _holderId.Value = holderId;

        /// <summary>
        /// 清除当前上下文的持有者 ID（通常在操作结束时调用）
        /// </summary>
        public static void Clear() => _holderId.Value = string.Empty;

        private static string GenerateHolderId()
        {
            // 推荐组合方式：机器名+进程ID+线程ID+随机数，保证跨机器/进程唯一
            return $"{Environment.MachineName}:{Environment.ProcessId}:{Thread.CurrentThread.ManagedThreadId}:{Guid.NewGuid():N}";
        }
    }
}