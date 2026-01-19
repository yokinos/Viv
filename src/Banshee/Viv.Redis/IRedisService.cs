using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Redis
{
    public interface IRedisService
    {
        bool Add(string key, object value, int seconds = 600) => Add(key, value, TimeSpan.FromSeconds(seconds));
        bool Add(string key, object value, TimeSpan expire);
        Task<bool> AddAsync(string key, object value, int seconds = 600) => AddAsync(key, value, TimeSpan.FromSeconds(seconds));
        Task<bool> AddAsync(string key, object value, TimeSpan expire);
        bool DelayExpire(string key, TimeSpan delayTime);
        Task<bool> DelayExpireAsync(string key, TimeSpan delayTime);
        bool Exist(string key);
        Task<bool> ExistAsync(string key);
        object Get(string key);
        T Get<T>(string key);
        Task<object> GetAsync(string key);
        Task<T> GetAsync<T>(string key);
        long Publish(string channel, object message);
        Task<long> PublishAsync(string channel, object message);
        long Remove(List<string> keyList);
        bool Remove(string key);
        Task<long> RemoveAsync(List<string> keyList);
        Task<bool> RemoveAsync(string key);
        bool SetKeyExpire(string key, TimeSpan expire);
        Task<bool> SetKeyExpireAsync(string key, TimeSpan expire);
        T Subscribe<T>(string channel);
        void Subscribe<T>(string channel, Action<T> action);
        Task<T> SubscribeAsync<T>(string channel);
        Task SubscribeAsync<T>(string channel, Action<T> action);
        void Unsubscribe(string channel);
        void UnsubscribeAll();
        Task UnsubscribeAllAsync();
        Task UnsubscribeAsync(string channel);
        bool HashSet(string key, string field, object value);
        Task<bool> HashSetAsync(string key, string field, object value);
        T HashGet<T>(string key, string field);
        Task<T> HashGetAsync<T>(string key, string field);
        bool HashExist(string key, string field);
        Task<bool> HashExistAsync(string key, string field);
        long HashRemove(string key, params string[] fields);
        Task<long> HashRemoveAsync(string key, params string[] fields);
        Dictionary<string, T> HashGetAll<T>(string key);
        Task<Dictionary<string, T>> HashGetAllAsync<T>(string key);
        long ListPush(string key, object value);
        Task<long> ListPushAsync(string key, object value);
        object ListPop(string key);
        Task<object> ListPopAsync(string key);
        List<T> ListRange<T>(string key, long start = 0, long stop = -1);
        Task<List<T>> ListRangeAsync<T>(string key, long start = 0, long stop = -1);
        long ListRemove(string key, object value, long count = 0);
        Task<long> ListRemoveAsync(string key, object value, long count = 0);
        bool TryLock(string lockKey, string lockValue, TimeSpan expire);
        Task<bool> TryLockAsync(string lockKey, string lockValue, TimeSpan expire);
        bool ReleaseLock(string lockKey, string lockValue);
        Task<bool> ReleaseLockAsync(string lockKey, string lockValue);
    }
}
