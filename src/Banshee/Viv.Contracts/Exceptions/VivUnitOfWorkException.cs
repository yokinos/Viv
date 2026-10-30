using System;

namespace Viv.Contracts.Exceptions
{
    /// <summary>
    /// 工作单元提交被拒绝。
    ///
    /// 目前唯一来源：嵌套事务的 <c>commitWhen</c> 拒绝提交后，最外层仍正常返回。
    /// 数据库已经回滚，不能再把这次调用当成成功 ——
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
