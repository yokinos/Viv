using System;
using System.Collections.Generic;
using Viv.Contracts.Interface;

namespace Viv.Momo.DataFilter
{
    /// <summary>
    /// 数据过滤器作用域
    /// 关掉的状态仍在 DataFilterSwitch 的静态集合里，这里只揣着开作用域那一刻的快照，释放时写回去
    /// </summary>
    internal sealed class DataFilterScope : IDataFilterScope
    {
        private readonly HashSet<Type>? _previous;
        private bool _disposed;

        internal DataFilterScope(HashSet<Type>? previous) => _previous = previous;

        public IDataFilterScope Disable<TFilter>() where TFilter : class => Disable(typeof(TFilter));

        public IDataFilterScope Disable(Type filterType)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            DataFilterSwitch.AddFilter(filterType);
            return this;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            DataFilterSwitch.Restore(_previous);
        }
    }
}
