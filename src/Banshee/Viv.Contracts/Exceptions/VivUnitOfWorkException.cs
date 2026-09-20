using System;

namespace Viv.Contracts.Exceptions
{
    /// <summary>
    /// 工作单元提交被拒绝。
    ///
    /// 目前唯一来源：嵌套事务把作用域打成 rollback-only 后，最外层仍调用
    /// <c>CommitAsync</c>。数据库已经回滚，不能再把这次调用当成成功 ——
    /// 拦截器必须看见失败，本地事件才能 Discard 而不是 Flush。
    /// </summary>
    public class VivUnitOfWorkException : Exception
    {
        public VivUnitOfWorkException() { }

        public VivUnitOfWorkException(string message) : base(message) { }

        public VivUnitOfWorkException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}
