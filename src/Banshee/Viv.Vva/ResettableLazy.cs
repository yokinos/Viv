using System;
using System.Threading;

namespace Viv.Vva
{
    /// <summary>
    /// 可重置的懒加载包装类（扩展原生Lazy<T>，支持重置后重新懒加载）
    /// </summary>
    /// <typeparam name="T">懒加载的目标类型</typeparam>
    /// <remarks>
    /// 1. 完全兼容原生Lazy<T>的用法（IsValueCreated/Value）；
    /// 2. 线程安全：默认使用LazyThreadSafetyMode.ExecutionAndPublication保证多线程安全；
    /// 3. 重置后会销毁原有Lazy实例，下次访问Value时重新执行工厂方法。
    /// </remarks>
    public class ResettableLazy<T>
    {
        private readonly Func<T> _factory;
        private readonly Lock _lock = new();
        private Lazy<T> _lazy;
        private readonly LazyThreadSafetyMode _mode;

        /// <summary>
        /// 初始化可重置的懒加载实例
        /// </summary>
        /// <param name="factory">创建目标实例的工厂方法（不可为null）</param>
        /// <exception cref="ArgumentNullException">工厂方法为null时抛出</exception>
        public ResettableLazy(Func<T> factory) : this(factory, LazyThreadSafetyMode.ExecutionAndPublication)
        {

        }

        /// <summary>
        /// 初始化可重置的懒加载实例
        /// </summary>
        /// <param name="factory">创建目标实例的工厂方法（不可为null）</param>
        /// <param name="mode">线程安全模式（推荐使用ExecutionAndPublication）</param>
        /// <exception cref="ArgumentNullException">工厂方法为null时抛出</exception>
        public ResettableLazy(Func<T> factory, LazyThreadSafetyMode mode)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory), "懒加载工厂方法不可为null，请传入有效的创建逻辑");
            _lazy = new Lazy<T>(factory, mode);
            _mode = mode;
        }

        /// <summary>
        /// 获取一个值，指示是否已创建目标实例
        /// </summary>
        public bool IsValueCreated => _lazy.IsValueCreated;

        /// <summary>
        /// 获取懒加载的目标实例
        /// </summary>
        public T Value => _lazy.Value;

        /// <summary>
        /// 重置懒加载实例（下次访问Value时重新执行工厂方法创建新实例）
        /// </summary>
        /// <remarks>线程安全，多线程并发调用Reset不会导致异常</remarks>
        public void Reset(bool forceCreate = false)
        {
            lock (_lock)
            {
                _lazy = new Lazy<T>(_factory, _mode);
                if (forceCreate)
                {
                    _ = Value;
                }
            }
        }
    }
}