using System;
using System.Collections.Generic;
using System.Text;
using Viv.Entity.Enums;

namespace Viv.Elysia
{
    /// <summary>
    /// 操作日志业务上下文（纯数据载体）
    /// </summary>
    public class OperationLogContext
    {
        public OperationLogContext() { }

        public OperationLogContext(EmOperationModule module, EmOperationType operation, string? description = null)
        {
            Module = module;
            Operation = operation;
            Description = description;
            IsRecord = true;
        }

        /// <summary>
        /// 是否记录（默认 true）
        /// </summary>
        public bool IsRecord { get; set; } = true;

        /// <summary>
        /// 业务是否已调用 SetLog。filter 预置容器时保持 false，SetLog 置位——
        /// 区分「未声明记录意图」与「明确不记录」，避免误发布未标注操作日志的请求。
        /// </summary>
        public bool IsSet { get; set; }

        /// <summary>
        /// 功能模块
        /// </summary>
        public EmOperationModule Module { get; set; }

        /// <summary>
        /// 操作类型
        /// </summary>
        public EmOperationType Operation { get; set; }

        /// <summary>
        /// 业务操作描述
        /// </summary>
        public string? Description { get; set; }
    }
}
