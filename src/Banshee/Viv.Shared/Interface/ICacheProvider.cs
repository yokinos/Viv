using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Shared.Interface
{
    /// <summary>   
    /// 缓存使用约定接口
    /// </summary>
    public interface ICacheProvider
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
