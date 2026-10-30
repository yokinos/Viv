using System.Collections.Concurrent;

using Microsoft.Extensions.DependencyInjection;

namespace Viv.Skirk.Core
{
    /// <summary>
    /// TaskType → handler 类型的映射。启动后第一次解析时建好，之后只查字典。
    ///
    /// 不存 CLR 类型到库里：类改名/拆程序集不会让历史任务失效，也避免反序列化任意类型。
    /// </summary>
    public sealed class VivAsyncTaskHandlerRegistry
    {
        private readonly ConcurrentDictionary<string, Type> _handlers = new(StringComparer.OrdinalIgnoreCase);

        private readonly object _gate = new();

        private volatile bool _built;

        /// <summary>找不到返回 null（Worker 会把它标成 Failed，而不是卡在队列里）</summary>
        public Type? ResolveHandlerType(string taskType, IServiceProvider scope)
        {
            Build(scope);

            return _handlers.TryGetValue(taskType, out var type) ? type : null;
        }

        /// <summary>启动时建表。TaskType 重复在这里抛，不等到第一条任务。</summary>
        public void Build(IServiceProvider scope) => EnsureBuilt(scope);

        private void EnsureBuilt(IServiceProvider scope)
        {
            if (_built) return;

            lock (_gate)
            {
                if (_built) return;

                foreach (var handler in scope.GetServices<IVivAsyncTaskHandler>())
                {
                    var taskType = handler.TaskType;
                    var type = handler.GetType();

                    if (_handlers.TryGetValue(taskType, out var existing) && existing != type)
                    {
                        throw new InvalidOperationException(
                            $"TaskType 重复注册：'{taskType}' 同时被 {existing.FullName} 和 {type.FullName} 声明。");
                    }

                    _handlers[taskType] = type;
                }

                _built = true;
            }
        }
    }
}
