using System;
using Viv.Entity.Enums;

namespace Viv.Elysia
{
    /// <summary>
    /// 操作日志上下文访问器（AsyncLocal 载体）
    /// </summary>
    public static class ElysiaLogContextAccessor
    {
        private static readonly AsyncLocal<OperationLogContext?> _current = new();

        public static OperationLogContext? Current => _current.Value;

        public static void Set(OperationLogContext context)
        {
            _current.Value = context;
        }

        public static void Clear()
        {
            _current.Value = null;
        }

        public static void SetLog(EmOperationModule module, EmOperationType operation, string? description = null, bool isRecord = true)
        {
            var holder = _current.Value;
            if (holder == null)
            {
                // 无预置容器（worker/非 filter 流程）→ 独立上下文
                _current.Value = new OperationLogContext(module, operation, description)
                {
                    IsRecord = isRecord,
                    IsSet = true
                };
            }
            else
            {
                // filter 已预置可变容器 → 改字段（引用不变，父 await 后仍可读）
                holder.Module = module;
                holder.Operation = operation;
                holder.Description = description;
                holder.IsRecord = isRecord;
                holder.IsSet = true;
            }
        }
    }

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

        /// <summary>
        /// 请求参数（覆盖自动记录的）
        /// </summary>
        public string? RequestJson { get; set; }

        /// <summary>
        /// 响应结果（覆盖自动记录的）
        /// </summary>
        public string? ResponseJson { get; set; }
    }

}