using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using System;
using System.Threading;
using System.Threading.Tasks;
using Viv.Contracts.Interface;

namespace Viv.Engine.Cache
{
    public class MemoryCacheService : IMemoryCacheService
    {
        private readonly IMemoryCache _cache;

        public MemoryCacheService(IMemoryCache cache)
        {
            _cache = cache;
        }

        public bool Exists(string key)
        {
            return _cache.TryGetValue(key, out _);
        }

        public async Task<bool> ExistsAsync(string key, CancellationToken token = default)
        {
            await Task.CompletedTask;
            return Exists(key);
        }

        public T Get<T>(string key)
        {
            return _cache.Get<T>(key);
        }

        public async Task<T> GetAsync<T>(string key, CancellationToken token = default)
        {
            await Task.CompletedTask;
            return Get<T>(key);
        }

        public bool Remove(string key)
        {
            _cache.Remove(key);
            return true;
        }

        public async Task<bool> RemoveAsync(string key, CancellationToken token = default)
        {
            await Task.CompletedTask;
            return Remove(key);
        }

        public bool Set<T>(string key, T value, TimeSpan? expire = null)
        {
            if (expire.HasValue)
            {
                _cache.Set(key, value, expire.Value);
            }
            else
            {
                _cache.Set(key, value);
            }
            return true;
        }

        public async Task<bool> SetAsync<T>(string key, T value, TimeSpan? expire = null, CancellationToken token = default)
        {
            await Task.CompletedTask;
            return Set(key, value, expire);
        }
    }
}