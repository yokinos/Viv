namespace Viv.Contracts.Interface
{
    /// <summary>
    /// 内存缓存接口
    /// </summary>
    public interface IMemoryCacheService
    {
        /// <summary>
        /// 获取缓存值，key 不存在时返回 default
        /// </summary>
        T? Get<T>(string key);

        /// <summary>
        /// 尝试获取缓存值
        /// </summary>
        bool TryGet<T>(string key, out T? value);

        /// <summary>
        /// 获取或添加：命中返回缓存值，未命中则执行 factory 并缓存后返回
        /// </summary>
        T GetOrAdd<T>(string key, Func<T> factory, TimeSpan? expire = null);

        /// <summary>
        /// 获取或添加（异步工厂）：factory 支持异步操作（如数据库查询）
        /// </summary>
        ValueTask<T?> GetOrAddAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory, TimeSpan? expire = null, CancellationToken token = default);

        /// <summary>
        /// 设置缓存
        /// </summary>
        void Set<T>(string key, T value, TimeSpan? expire = null);

        /// <summary>
        /// 移除缓存
        /// </summary>
        void Remove(string key);
    }
}
