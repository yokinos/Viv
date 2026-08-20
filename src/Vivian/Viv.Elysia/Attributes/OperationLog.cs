using System;

namespace Viv.Elysia.Attributes
{
    /// <summary>
    /// 标记当前 API 是否自动记录操作日志
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public class OperationLogAttribute : Attribute
    {
        public OperationLogAttribute() { }
    }
}