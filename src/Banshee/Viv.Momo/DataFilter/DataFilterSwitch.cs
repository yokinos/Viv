using System;
using System.Collections.Generic;
using System.Threading;
using Viv.Contracts.Interface;

namespace Viv.Momo.DataFilter
{
    /// <summary>
    /// 数据过滤器开关
    /// 状态在静态 AsyncLocal 的「已关掉的过滤器」集合里，实例无状态。
    /// EF 模型读的 IsDataFilterDisabled 也是这份值，所以单例注册与静态注册没有区别。
    /// </summary>
    public sealed class DataFilterSwitch : IDataFilter
    {
        private static readonly AsyncLocal<HashSet<Type>?> _disabledFilters = new();

        public IDisposable Disable<TFilter>() where TFilter : class => Disable(typeof(TFilter));

        public bool IsDisabled<TFilter>() where TFilter : class => IsDisabled(typeof(TFilter));

        public IDataFilterScope Scope() => new DataFilterScope(_disabledFilters.Value);

        /// <summary>
        /// 按过滤器类型关掉 / 问它是不是关着
        /// 框架内部走这两个（EFAppContext 建表达式、MomoDatabaseContext 拼按主键 SQL），泛型那对给业务用
        /// </summary>
        internal static IDisposable Disable(Type filterType) => DisableCore(filterType);

        /// <summary>
        /// filterType 对应的过滤器是否已关掉
        /// </summary>
        internal static bool IsDisabled(Type filterType) => _disabledFilters.Value?.Contains(filterType) == true;

        /// <summary>
        /// 往当前关掉的集合里再加一条，没有句柄
        /// 换一个新集合而不是就地改，因为旧集合可能还被别的句柄当快照揣着
        /// </summary>
        internal static void AddFilter(Type filterType)
        {
            var previous = _disabledFilters.Value;
            var next = previous == null ? new HashSet<Type>() : new HashSet<Type>(previous);
            next.Add(filterType);

            _disabledFilters.Value = next;
        }

        /// <summary>
        /// 把集合恢复成某份快照
        /// </summary>
        internal static void Restore(HashSet<Type>? snapshot) => _disabledFilters.Value = snapshot;

        private static IDisposable DisableCore(Type filterType)
        {
            var previous = _disabledFilters.Value;
            AddFilter(filterType);
            return new RestoreScope(previous);
        }

        private sealed class RestoreScope : IDisposable
        {
            private readonly HashSet<Type>? _previous;
            private bool _disposed;

            public RestoreScope(HashSet<Type>? previous) => _previous = previous;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                // 恢复整个快照而不是只摘掉自己那个，嵌套才正确：
                // 外层关 A、内层关 B，内层释放后 A 仍然关着，外层释放才回到最初
                _disabledFilters.Value = _previous;
            }
        }
    }
}
