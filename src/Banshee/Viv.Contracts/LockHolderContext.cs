using System.Threading;
using Viv.Delusion.Extension;
using Viv.Delusion.Magic;

namespace Viv.Contracts
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

        /// <summary>
        /// 重置当前异步流中的锁持有者
        /// </summary>
        public static void ResetHolderId() => _holderId.Value = GenerateHolderId();

        /// <summary>
        /// 显式设置持有者Id
        /// </summary>
        public static void SetHolderId(string holderId) => _holderId.Value = holderId;

        /// <summary>
        /// 清除当前上下文的持有者 ID（通常在操作结束时调用）
        /// </summary>
        public static void Clear() => _holderId.Value = string.Empty;

        /// <summary>
        /// 生成分布式锁持有者 Id 并写入当前异步流。
        /// </summary>
        public static string GenerateHolderId()
        {
            var id = IdMagic.NextId(1023).ToString();
            _holderId.Value = id;
            return id;
        }
    }
}