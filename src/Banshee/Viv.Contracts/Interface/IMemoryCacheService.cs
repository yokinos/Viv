using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Contracts.Interface
{
    /// <summary>   
    /// 内存缓存接口
    /// </summary>
    public interface IMemoryCacheService
    {
        T Get<T>(string key);

        Task<T> GetAsync<T>(string key, CancellationToken token = default);

        bool Set<T>(string key, T value, TimeSpan? expire = null);

        Task<bool> SetAsync<T>(string key, T value, TimeSpan? expire = null, CancellationToken token = default);

        bool Remove(string key);

        Task<bool> RemoveAsync(string key, CancellationToken token = default);

        bool Exists(string key);

        Task<bool> ExistsAsync(string key, CancellationToken token = default);
    }
}
