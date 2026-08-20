using System;
using Viv.Entity.Enums;

namespace Viv.Elysia.Attributes
{
    /// <summary>
    /// 标记当前 API 是否自动记录操作日志 这个会以返回结果的Message为日志内容
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public class OperationLogAttribute : Attribute
    {
        public OperationLogAttribute(EmOperationModule module, EmOperationType operation) : this(module, operation, 200) { }

        public OperationLogAttribute(EmOperationModule module, EmOperationType operation, params int[] codes)
        {
            Module = module;
            Operation = operation;
            Codes = codes;
        }

        /// <summary>
        /// 哪些状态码会记录日志
        /// </summary>
        public int[] Codes { get; set; }

        /// <summary>
        /// 功能模块
        /// </summary>
        public EmOperationModule Module { get; set; }

        /// <summary>
        /// 操作类型
        /// </summary>
        public EmOperationType Operation { get; set; }
    }
}