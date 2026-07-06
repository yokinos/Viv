using Microsoft.Extensions.Caching.Memory;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Viv.Contracts.Interface;

namespace Viv.Sandrone.Impl
{
    public class MemoryCacheService : IMemoryCacheService
    {
        private readonly IMemoryCache _cache;

        public MemoryCacheService(IMemoryCache cache)
        {
            _cache = cache;
        }

        [return: MaybeNull]
        public T? Get<T>(string key)
        {
            _cache.TryGetValue(key, out T? value);
            return value;
        }

        [return: MaybeNull]
        public bool TryGet<T>(string key, out T? value)
        {
            return _cache.TryGetValue(key, out value);
        }

        public T GetOrAdd<T>(string key, Func<T> factory, TimeSpan? expire = null)
        {
            return _cache.GetOrCreate(key, entry =>
            {
                if (expire.HasValue)
                    entry.SetAbsoluteExpiration(expire.Value);
                return factory();
            })!;
        }

        [return: MaybeNull]
        public async ValueTask<T?> GetOrAddAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory,
            TimeSpan? expire = null, CancellationToken token = default)
        {
            if (_cache.TryGetValue(key, out T? cached))
                return cached;

            var value = await factory(token);

            using var entry = _cache.CreateEntry(key);
            if (expire.HasValue)
                entry.SetAbsoluteExpiration(expire.Value);
            entry.Value = value;

            return value;
        }

        public void Set<T>(string key, T value, TimeSpan? expire = null)
        {
            if (expire.HasValue)
                _cache.Set(key, value, expire.Value);
            else
                _cache.Set(key, value);
        }

        public void Remove(string key)
        {
            _cache.Remove(key);
        }
    }
}