using System;

namespace Viv.Engine.Locking
{
    /// <summary>
    /// 锁 Key 解析失败：模板占位符解析为空、<see cref="Viv.Contracts.Interface.ILockKey"/> 返回空串等。
    /// 运行期直接抛出，不做降级 —— 空 key 会退化成全局锁，比报错危险得多。
    /// </summary>
    public sealed class DistributedLockKeyException : InvalidOperationException
    {
        public DistributedLockKeyException(string message) : base(message) { }
    }
}
