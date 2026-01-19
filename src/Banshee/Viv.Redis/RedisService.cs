using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Vva.Extension;

#nullable disable
namespace Viv.Redis
{
    public class RedisService : RedisFactory, IRedisService
    {
        public bool Add(string key, object value, TimeSpan expire)
        {
            if (value == null) return false;
            return TryExecute(key, x => x.StringSet(key, value.ToJson(), expire));
        }

        public async Task<bool> AddAsync(string key, object value, TimeSpan expire)
        {
            if (value == null) return false;
            return await TryExecuteAsync(key, x => x.StringSetAsync(key, value.ToJson(), expire));
        }

        public bool DelayExpire(string key, TimeSpan delayTime)
        {
            return TryExecute(key, x =>
            {
                var expire = x.KeyExpireTime(key);
                if (expire != null)
                {
                    return x.KeyExpire(key, expire.Value + delayTime);
                }

                return false;
            });
        }

        public async Task<bool> DelayExpireAsync(string key, TimeSpan delayTime)
        {
            return await TryExecuteAsync(key, async x =>
            {
                var expire = await x.KeyExpireTimeAsync(key);
                if (expire != null)
                {
                    return await x.KeyExpireAsync(key, expire.Value + delayTime);
                }

                return false;
            });
        }

        public bool Exist(string key)
        {
            throw new NotImplementedException();
        }

        public Task<bool> ExistAsync(string key)
        {
            throw new NotImplementedException();
        }

        public object Get(string key)
        {
            throw new NotImplementedException();
        }

        public T Get<T>(string key)
        {
            throw new NotImplementedException();
        }

        public Task<object> GetAsync(string key)
        {
            throw new NotImplementedException();
        }

        public Task<T> GetAsync<T>(string key)
        {
            throw new NotImplementedException();
        }

        public bool HashExist(string key, string field)
        {
            throw new NotImplementedException();
        }

        public Task<bool> HashExistAsync(string key, string field)
        {
            throw new NotImplementedException();
        }

        public T HashGet<T>(string key, string field)
        {
            throw new NotImplementedException();
        }

        public Dictionary<string, T> HashGetAll<T>(string key)
        {
            throw new NotImplementedException();
        }

        public Task<Dictionary<string, T>> HashGetAllAsync<T>(string key)
        {
            throw new NotImplementedException();
        }

        public Task<T> HashGetAsync<T>(string key, string field)
        {
            throw new NotImplementedException();
        }

        public long HashRemove(string key, params string[] fields)
        {
            throw new NotImplementedException();
        }

        public Task<long> HashRemoveAsync(string key, params string[] fields)
        {
            throw new NotImplementedException();
        }

        public bool HashSet(string key, string field, object value)
        {
            throw new NotImplementedException();
        }

        public Task<bool> HashSetAsync(string key, string field, object value)
        {
            throw new NotImplementedException();
        }

        public object ListPop(string key)
        {
            throw new NotImplementedException();
        }

        public Task<object> ListPopAsync(string key)
        {
            throw new NotImplementedException();
        }

        public long ListPush(string key, object value)
        {
            throw new NotImplementedException();
        }

        public Task<long> ListPushAsync(string key, object value)
        {
            throw new NotImplementedException();
        }

        public List<T> ListRange<T>(string key, long start = 0, long stop = -1)
        {
            throw new NotImplementedException();
        }

        public Task<List<T>> ListRangeAsync<T>(string key, long start = 0, long stop = -1)
        {
            throw new NotImplementedException();
        }

        public long ListRemove(string key, object value, long count = 0)
        {
            throw new NotImplementedException();
        }

        public Task<long> ListRemoveAsync(string key, object value, long count = 0)
        {
            throw new NotImplementedException();
        }

        public long Publish(string channel, object message)
        {
            throw new NotImplementedException();
        }

        public Task<long> PublishAsync(string channel, object message)
        {
            throw new NotImplementedException();
        }

        public bool ReleaseLock(string lockKey, string lockValue)
        {
            throw new NotImplementedException();
        }

        public Task<bool> ReleaseLockAsync(string lockKey, string lockValue)
        {
            throw new NotImplementedException();
        }

        public long Remove(List<string> keyList)
        {
            throw new NotImplementedException();
        }

        public bool Remove(string key)
        {
            throw new NotImplementedException();
        }

        public Task<long> RemoveAsync(List<string> keyList)
        {
            throw new NotImplementedException();
        }

        public Task<bool> RemoveAsync(string key)
        {
            throw new NotImplementedException();
        }

        public bool SetKeyExpire(string key, TimeSpan expire)
        {
            throw new NotImplementedException();
        }

        public Task<bool> SetKeyExpireAsync(string key, TimeSpan expire)
        {
            throw new NotImplementedException();
        }

        public T Subscribe<T>(string channel)
        {
            throw new NotImplementedException();
        }

        public void Subscribe<T>(string channel, Action<T> action)
        {
            throw new NotImplementedException();
        }

        public Task<T> SubscribeAsync<T>(string channel)
        {
            throw new NotImplementedException();
        }

        public Task SubscribeAsync<T>(string channel, Action<T> action)
        {
            throw new NotImplementedException();
        }

        public bool TryLock(string lockKey, string lockValue, TimeSpan expire)
        {
            throw new NotImplementedException();
        }

        public Task<bool> TryLockAsync(string lockKey, string lockValue, TimeSpan expire)
        {
            throw new NotImplementedException();
        }

        public void Unsubscribe(string channel)
        {
            throw new NotImplementedException();
        }

        public void UnsubscribeAll()
        {
            throw new NotImplementedException();
        }

        public Task UnsubscribeAllAsync()
        {
            throw new NotImplementedException();
        }

        public Task UnsubscribeAsync(string channel)
        {
            throw new NotImplementedException();
        }
    }
}
