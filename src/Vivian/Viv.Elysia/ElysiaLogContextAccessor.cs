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
}