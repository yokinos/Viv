using Newtonsoft.Json.Linq;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Viv.Contracts.Interface;
using Viv.Redis.DbAllocator;
using Viv.Vva.Extension;

#nullable disable
namespace Viv.Redis
{
    /// <summary>
    /// Redis 业务操作实现类
    /// 继承 VivRedis 复用连接管理和通用执行逻辑，实现 IRedisService 接口规范
    /// 核心特性：
    /// 1. 自动序列化/反序列化（ToJson/As<T>）；
    /// 2. 内置空值校验、异常捕获；
    /// 3. 支持同步/异步双版本方法；
    /// 4. 分布式锁基于Lua脚本保证原子性；
    /// 5. 批量操作按Db分组执行，提升性能。
    /// </summary>
    public class RedisService : VivRedis, IRedisService
    {
        /// <summary>
        /// 构造函数
        /// 注意：
        /// 1. 无需在实例化时传入配置，需在程序启动时调用 RedisFactory.Initialize 初始化配置；
        /// 2. 运行中不支持切换Redis配置；
        /// 3. 若现有封装不满足需求，可直接调用 VivRedis.ExecuteRedis/ExecuteRedisAsync 或 GetDatabaseAsync 自定义操作。
        /// </summary>
        public RedisService()
        {

        }

        #region 基础字符串操作

        /// <summary>
        /// 新增字符串类型缓存
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="value">缓存值（自动序列化为JSON）</param>
        /// <param name="expire">过期时间</param>
        /// <returns>操作是否成功（值为null时返回false）</returns>
        public bool Add(string key, object value, TimeSpan expire)
        {
            if (value == null) return false;
            return ExecuteRedis(key, x => x.StringSet(key, value.ToJson(), expire));
        }

        /// <summary>
        /// 异步新增字符串类型缓存
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="value">缓存值（自动序列化为JSON）</param>
        /// <param name="expire">过期时间</param>
        /// <returns>操作是否成功（值为null时返回false）</returns>
        public async Task<bool> AddAsync(string key, object value, TimeSpan expire)
        {
            if (value == null) return false;
            return await ExecuteRedisAsync(key, x => x.StringSetAsync(key, value.ToJson(), expire));
        }

        /// <summary>
        /// 检查指定键是否存在
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <returns>存在返回true，否则返回false</returns>
        public bool Exist(string key)
        {
            return ExecuteRedis(key, x => x.KeyExists(key));
        }

        /// <summary>
        /// 异步检查指定键是否存在
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <returns>存在返回true，否则返回false</returns>
        public async Task<bool> ExistAsync(string key)
        {
            return await ExecuteRedisAsync(key, async x => await x.KeyExistsAsync(key));
        }

        /// <summary>
        /// 获取字符串类型缓存值（未反序列化）
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <returns>原始缓存值（键不存在返回null）</returns>
        public object Get(string key)
        {
            return ExecuteRedis(key, x => x.StringGet(key));
        }

        /// <summary>
        /// 获取字符串类型缓存值并反序列化为指定类型
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="key">Redis键</param>
        /// <returns>反序列化后的对象（键不存在返回默认值）</returns>
        [return: MaybeNull]
        public T Get<T>(string key)
        {
            return ExecuteRedis(key, x =>
            {
                var value = x.StringGet(key);
                if (value.IsNull) return default;
                return value.As<T>();
            });
        }

        /// <summary>
        /// 异步获取字符串类型缓存值（未反序列化）
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <returns>原始缓存值（键不存在返回null）</returns>
        public async Task<object> GetAsync(string key)
        {
            return await ExecuteRedisAsync(key, async x => await x.StringGetAsync(key));
        }

        /// <summary>
        /// 异步获取字符串类型缓存值并反序列化为指定类型
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="key">Redis键</param>
        /// <returns>反序列化后的对象（键不存在返回默认值）</returns>
        public async Task<T> GetAsync<T>(string key)
        {
            return await ExecuteRedisAsync(key, async x =>
            {
                var value = await x.StringGetAsync(key);
                if (value.IsNull) return default;
                return value.As<T>();
            });
        }

        #endregion

        #region 过期时间操作

        /// <summary>
        /// 延迟缓存过期时间（基于当前过期时间累加）
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="delayTime">需要延迟的时长</param>
        /// <returns>操作是否成功（键无过期时间时返回false）</returns>
        public bool DelayExpire(string key, TimeSpan delayTime)
        {
            return ExecuteRedis(key, x =>
            {
                var expire = x.KeyExpireTime(key);
                if (expire != null)
                {
                    return x.KeyExpire(key, expire.Value + delayTime);
                }

                return false;
            });
        }

        /// <summary>
        /// 异步延迟缓存过期时间（基于当前过期时间累加）
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="delayTime">需要延迟的时长</param>
        /// <returns>操作是否成功（键无过期时间时返回false）</returns>
        public async Task<bool> DelayExpireAsync(string key, TimeSpan delayTime)
        {
            return await ExecuteRedisAsync(key, async x =>
            {
                var expire = await x.KeyExpireTimeAsync(key);
                if (expire != null)
                {
                    return await x.KeyExpireAsync(key, expire.Value + delayTime);
                }

                return false;
            });
        }

        /// <summary>
        /// 设置键的过期时间
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="expire">过期时间（必须大于0）</param>
        /// <returns>操作是否成功</returns>
        public bool SetKeyExpire(string key, TimeSpan expire)
        {
            if (expire <= TimeSpan.Zero) return false;
            return ExecuteRedis(key, x => x.KeyExpire(key, expire));
        }

        /// <summary>
        /// 异步设置键的过期时间
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="expire">过期时间（必须大于0）</param>
        /// <returns>操作是否成功</returns>
        public async Task<bool> SetKeyExpireAsync(string key, TimeSpan expire)
        {
            if (expire <= TimeSpan.Zero) return false;
            return await ExecuteRedisAsync(key, async x => await x.KeyExpireAsync(key, expire));
        }

        #endregion

        #region 删除操作

        /// <summary>
        /// 删除单个Redis键
        /// </summary>
        /// <param name="key">需要删除的键</param>
        /// <returns>是否删除成功（键为空返回false）</returns>
        public bool Remove(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            return ExecuteRedis(key, x => x.KeyDelete(key));
        }

        /// <summary>
        /// 批量删除指定的Redis键
        /// </summary>
        /// <param name="keyList">需要删除的键列表</param>
        /// <returns>成功删除的键数量</returns>
        public long Remove(List<string> keyList)
        {
            var list = ExecuteRedis(keyList, (x, keys) => x.KeyDelete(keys));
            return list.Sum();
        }

        /// <summary>
        /// 异步删除单个Redis键
        /// </summary>
        /// <param name="key">需要删除的键</param>
        /// <returns>是否删除成功（键为空返回false）</returns>
        public async Task<bool> RemoveAsync(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            return await ExecuteRedisAsync(key, async x => await x.KeyDeleteAsync(key));
        }

        /// <summary>
        /// 异步批量删除指定的Redis键
        /// </summary>
        /// <param name="keyList">需要删除的键列表</param>
        /// <returns>成功删除的键数量</returns>
        public async Task<long> RemoveAsync(List<string> keyList)
        {
            var list = await ExecuteRedisAsync(keyList, async (x, keys) => await x.KeyDeleteAsync(keys));
            return list.Sum();
        }

        #endregion

        #region Hash操作

        /// <summary>
        /// 检查Hash类型缓存的指定字段是否存在
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="field">Hash字段名</param>
        /// <returns>存在返回true，否则返回false</returns>
        public bool HashExist(string key, string field)
        {
            return ExecuteRedis(key, x => x.HashExists(key, field));
        }

        /// <summary>
        /// 异步检查Hash类型缓存的指定字段是否存在
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="field">Hash字段名</param>
        /// <returns>存在返回true，否则返回false</returns>
        public async Task<bool> HashExistAsync(string key, string field)
        {
            return await ExecuteRedisAsync(key, async x => await x.HashExistsAsync(key, field));
        }

        /// <summary>
        /// 获取Hash类型缓存的指定字段值并反序列化为指定类型
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="key">Redis键</param>
        /// <param name="field">Hash字段名</param>
        /// <returns>反序列化后的字段值（字段不存在返回默认值）</returns>
        public T HashGet<T>(string key, string field)
        {
            return ExecuteRedis(key, x =>
            {
                var hashValue = x.HashGet(key, field);
                if (hashValue.IsNull) return default;
                return hashValue.As<T>();
            });
        }

        /// <summary>
        /// 异步获取Hash类型缓存的指定字段值并反序列化为指定类型
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="key">Redis键</param>
        /// <param name="field">Hash字段名</param>
        /// <returns>反序列化后的字段值（字段不存在返回默认值）</returns>
        public async Task<T> HashGetAsync<T>(string key, string field)
        {
            return await ExecuteRedisAsync(key, async x =>
            {
                var hashValue = await x.HashGetAsync(key, field);
                if (hashValue.IsNull) return default;
                return hashValue.As<T>();
            });
        }

        /// <summary>
        /// 获取Hash类型缓存的所有字段和值，并反序列化为指定类型的字典
        /// </summary>
        /// <typeparam name="T">值的目标类型</typeparam>
        /// <param name="key">Redis键</param>
        /// <returns>字段名-值字典（键不存在返回空字典）</returns>
        public Dictionary<string, T> HashGetAll<T>(string key)
        {
            return ExecuteRedis(key, x => x.HashGetAll(key).ToDictionary(x => x.Name.ToString(), x => x.Value.As<T>()));
        }

        /// <summary>
        /// 异步获取Hash类型缓存的所有字段和值，并反序列化为指定类型的字典
        /// </summary>
        /// <typeparam name="T">值的目标类型</typeparam>
        /// <param name="key">Redis键</param>
        /// <returns>字段名-值字典（键不存在返回空字典）</returns>
        public async Task<Dictionary<string, T>> HashGetAllAsync<T>(string key)
        {
            return await ExecuteRedisAsync(key, async x => (await x.HashGetAllAsync(key)).ToDictionary(x => x.Name.ToString(), x => x.Value.As<T>()));
        }

        /// <summary>
        /// 设置Hash类型缓存的字段值
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="field">Hash字段名</param>
        /// <param name="value">字段值（自动序列化为JSON）</param>
        /// <returns>操作是否成功（值为null时返回false）</returns>
        public bool HashSet(string key, string field, object value)
        {
            if (value == null) return false;
            var redisValue = value.ToJson(); // 复用已有序列化逻辑
            return ExecuteRedis(key, x => x.HashSet(key, field, redisValue));
        }

        /// <summary>
        /// 异步设置Hash类型缓存的字段值
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="field">Hash字段名</param>
        /// <param name="value">字段值（自动序列化为JSON）</param>
        /// <returns>操作是否成功（值为null时返回false）</returns>
        public async Task<bool> HashSetAsync(string key, string field, object value)
        {
            if (value == null) return false;
            var redisValue = value.ToJson();
            return await ExecuteRedisAsync(key, async x => await x.HashSetAsync(key, field, redisValue));
        }

        /// <summary>
        /// 删除Hash类型缓存的一个或多个字段
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="fields">需要删除的字段名数组</param>
        /// <returns>成功删除的字段数量</returns>
        public long HashRemove(string key, params string[] fields)
        {
            if (fields == null || fields.Length == 0) return 0;
            // 转换字段为 RedisValue 数组
            var redisFields = Array.ConvertAll(fields, f => (RedisValue)f);
            return ExecuteRedis(key, x => x.HashDelete(key, redisFields));
        }

        /// <summary>
        /// 异步删除Hash类型缓存的一个或多个字段
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="fields">需要删除的字段名数组</param>
        /// <returns>成功删除的字段数量</returns>
        public async Task<long> HashRemoveAsync(string key, params string[] fields)
        {
            if (fields == null || fields.Length == 0) return 0;
            var redisFields = Array.ConvertAll(fields, f => (RedisValue)f);
            return await ExecuteRedisAsync(key, async x => await x.HashDeleteAsync(key, redisFields));
        }

        #endregion

        #region List操作

        /// <summary>
        /// 向List类型缓存的尾部（右侧）添加元素
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="value">元素值（自动序列化为JSON）</param>
        /// <returns>添加元素后列表的长度（值为null时返回0）</returns>
        public long ListPush(string key, object value)
        {
            if (value == null) return 0;
            var redisValue = value.ToJson();
            // 从列表尾部推入（可根据需求改为 ListLeftPush）
            return ExecuteRedis(key, x => x.ListRightPush(key, redisValue));
        }

        /// <summary>
        /// 异步向List类型缓存的尾部（右侧）添加元素
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="value">元素值（自动序列化为JSON）</param>
        /// <returns>添加元素后列表的长度（值为null时返回0）</returns>
        public async Task<long> ListPushAsync(string key, object value)
        {
            if (value == null) return 0;
            var redisValue = value.ToJson();
            return await ExecuteRedisAsync(key, async x => await x.ListRightPushAsync(key, redisValue));
        }

        /// <summary>
        /// 从List类型缓存的尾部（右侧）弹出一个元素
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <returns>弹出的原始元素值（列表为空返回null）</returns>
        public object ListPop(string key)
        {
            return ExecuteRedis(key, x =>
            {
                var value = x.ListRightPop(key);
                return value.IsNull ? default : value;
            });
        }

        /// <summary>
        /// 异步从List类型缓存的尾部（右侧）弹出一个元素
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <returns>弹出的原始元素值（列表为空返回null）</returns>
        public async Task<object> ListPopAsync(string key)
        {
            return await ExecuteRedisAsync(key, async x =>
            {
                var value = await x.ListRightPopAsync(key);
                return value.IsNull ? default : value;
            });
        }

        /// <summary>
        /// 获取List类型缓存中指定范围的元素并反序列化为指定类型
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="key">Redis键</param>
        /// <param name="start">起始索引（0表示第一个元素）</param>
        /// <param name="stop">结束索引（-1表示最后一个元素）</param>
        /// <returns>指定范围的元素列表（键不存在/元素为空返回空列表）</returns>
        public List<T> ListRange<T>(string key, long start = 0, long stop = -1)
        {
            return ExecuteRedis(key, x =>
            {
                var values = x.ListRange(key, start, stop);
                var result = new List<T>();
                foreach (var value in values)
                {
                    if (!value.IsNull)
                    {
                        result.Add(value.As<T>());
                    }
                }
                return result;
            });
        }

        /// <summary>
        /// 异步获取List类型缓存中指定范围的元素并反序列化为指定类型
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="key">Redis键</param>
        /// <param name="start">起始索引（0表示第一个元素）</param>
        /// <param name="stop">结束索引（-1表示最后一个元素）</param>
        /// <returns>指定范围的元素列表（键不存在/元素为空返回空列表）</returns>
        public async Task<List<T>> ListRangeAsync<T>(string key, long start = 0, long stop = -1)
        {
            return await ExecuteRedisAsync(key, async x =>
            {
                var values = await x.ListRangeAsync(key, start, stop);
                var result = new List<T>();
                foreach (var value in values)
                {
                    if (!value.IsNull)
                    {
                        result.Add(value.As<T>());
                    }
                }
                return result;
            });
        }

        /// <summary>
        /// 从List类型缓存中删除指定数量的匹配元素
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="value">需要删除的元素值（自动序列化为JSON）</param>
        /// <param name="count">删除数量：0=删除所有匹配项；正数=删除前N个；负数=删除后N个</param>
        /// <returns>成功删除的元素数量（值为null时返回0）</returns>
        public long ListRemove(string key, object value, long count = 0)
        {
            if (value == null) return 0;
            var redisValue = value.ToJson();
            return ExecuteRedis(key, x => x.ListRemove(key, redisValue, count));
        }

        /// <summary>
        /// 异步从List类型缓存中删除指定数量的匹配元素
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="value">需要删除的元素值（自动序列化为JSON）</param>
        /// <param name="count">删除数量：0=删除所有匹配项；正数=删除前N个；负数=删除后N个</param>
        /// <returns>成功删除的元素数量（值为null时返回0）</returns>
        public async Task<long> ListRemoveAsync(string key, object value, long count = 0)
        {
            if (value == null) return 0;
            var redisValue = value.ToJson();
            return await ExecuteRedisAsync(key, async x => await x.ListRemoveAsync(key, redisValue, count));
        }

        #endregion

        #region 发布订阅

        /// <summary>
        /// 发布消息到指定频道
        /// </summary>
        /// <param name="channel">Redis频道</param>
        /// <param name="message">消息内容（自动序列化为JSON）</param>
        /// <returns>接收消息的客户端数量（消息为null时返回0）</returns>
        public long Publish(RedisChannel channel, object message)
        {
            if (message == null) return 0;
            var redisMessage = message.ToJson();
            return ExecuteRedis(channel, x => x.Publish(channel, redisMessage));
        }

        /// <summary>
        /// 异步发布消息到指定频道
        /// </summary>
        /// <param name="channel">Redis频道</param>
        /// <param name="message">消息内容（自动序列化为JSON）</param>
        /// <returns>接收消息的客户端数量（消息为null时返回0）</returns>
        public async Task<long> PublishAsync(RedisChannel channel, object message)
        {
            if (message == null) return 0;
            var redisMessage = message.ToJson();
            return await ExecuteRedisAsync(channel, async x => await x.PublishAsync(channel, redisMessage));
        }

        /// <summary>
        /// 订阅指定频道的消息
        /// </summary>
        /// <typeparam name="T">消息反序列化目标类型</typeparam>
        /// <param name="channel">Redis频道</param>
        /// <param name="action">消息接收后的处理回调</param>
        public void Subscribe<T>(RedisChannel channel, Action<T> action)
        {
            if (string.IsNullOrEmpty(channel) || action == null) return;
            ExecuteRedis(channel, x =>
            {
                var subscriber = x.Multiplexer.GetSubscriber();
                subscriber.Subscribe(channel, (ch, msg) =>
                {
                    if (!msg.IsNull)
                    {
                        var data = msg.As<T>();
                        action.Invoke(data);
                    }
                });
                return true;
            });
        }

        /// <summary>
        /// 异步订阅指定频道的消息
        /// </summary>
        /// <typeparam name="T">消息反序列化目标类型</typeparam>
        /// <param name="channel">Redis频道</param>
        /// <param name="action">消息接收后的处理回调</param>
        /// <returns>异步任务</returns>
        public async Task SubscribeAsync<T>(RedisChannel channel, Action<T> action)
        {
            if (string.IsNullOrEmpty(channel) || action == null) return;
            await ExecuteRedisAsync(channel, async x =>
            {
                var subscriber = x.Multiplexer.GetSubscriber();
                await subscriber.SubscribeAsync(channel, async (ch, msg) =>
                {
                    if (!msg.IsNull)
                    {
                        var data = msg.As<T>();
                        action.Invoke(data);
                    }
                });
                return true;
            });
        }

        /// <summary>
        /// 取消订阅指定频道
        /// </summary>
        /// <param name="channel">Redis频道</param>
        public void Unsubscribe(RedisChannel channel)
        {
            if (string.IsNullOrEmpty(channel)) return;
            ExecuteRedis(channel, x =>
            {
                var subscriber = x.Multiplexer.GetSubscriber();
                subscriber.Unsubscribe(channel);
                return true;
            });
        }

        /// <summary>
        /// 取消所有频道的订阅
        /// </summary>
        public void UnsubscribeAll()
        {
            ExecuteRedis(string.Empty, x =>
            {
                var subscriber = x.Multiplexer.GetSubscriber();
                subscriber.UnsubscribeAll();
                return true;
            });
        }

        /// <summary>
        /// 异步取消订阅指定频道
        /// </summary>
        /// <param name="channel">Redis频道</param>
        /// <returns>异步任务</returns>
        public async Task UnsubscribeAsync(RedisChannel channel)
        {
            if (string.IsNullOrEmpty(channel)) return;
            await ExecuteRedisAsync(channel, async x =>
            {
                var subscriber = x.Multiplexer.GetSubscriber();
                await subscriber.UnsubscribeAsync(channel);
                return true;
            });
        }

        /// <summary>
        /// 异步取消所有频道的订阅
        /// </summary>
        /// <returns>异步任务</returns>
        public async Task UnsubscribeAllAsync()
        {
            await ExecuteRedisAsync(string.Empty, async x =>
            {
                var subscriber = x.Multiplexer.GetSubscriber();
                await subscriber.UnsubscribeAllAsync();
                return true;
            });
        }

        #endregion

        #region 分布式锁

        /// <summary>
        /// 重入锁释放时的临时续期时间（秒），防止释放过程中锁过期
        /// </summary>
        private const int ReentrantLockTempExpireSeconds = 60;

        /// <summary>
        /// 获取可重入分布式锁
        /// </summary>
        /// <param name="lockKey">锁的唯一标识（如：stock_lock_1001）</param>
        /// <param name="lockHolderId">锁持有者唯一标识</param>
        /// <param name="expire">锁过期时间（必须>0，防止死锁）</param>
        /// <param name="isReentrant">是否启用重入，默认true</param>
        /// <returns>true=加锁/重入成功，false=加锁失败</returns>
        public bool AcquireLock(string lockKey, string lockHolderId, TimeSpan expire, bool isReentrant = true)
        {
            if (string.IsNullOrWhiteSpace(lockHolderId) || expire <= TimeSpan.Zero)
                return false;

            return ExecuteRedis(lockKey, db =>
            {
                // 非重入锁：原生SET NX EX逻辑，原子性
                if (!isReentrant)
                {
                    return db.StringSet(lockKey, lockHolderId, expire, When.NotExists);
                }

                // 可重入锁核心Lua脚本（原子性执行加锁/重入逻辑）
                var reentrantLockScript = @"
                    local currentVal = redis.call('GET', KEYS[1])
                    -- 情况1：锁未被持有 → 新增锁，格式：clientId_重入次数
                    if currentVal == false then
                        redis.call('SET', KEYS[1], ARGV[1] .. '_1', 'EX', ARGV[2])
                        return 1
                    -- 情况2：锁已被当前客户端持有 → 重入次数+1，续期过期时间
                    elseif string.sub(currentVal, 1, -2) == ARGV[1] then
                        local count = tonumber(string.sub(currentVal, -1)) + 1
                        redis.call('SET', KEYS[1], ARGV[1] .. '_' .. count, 'EX', ARGV[2])
                        return 1
                    -- 情况3：锁被其他客户端持有 → 加锁失败
                    else
                        return 0
                    end";

                // 执行Lua脚本：KEYS[1]=lockKey，ARGV[1]=clientId，ARGV[2]=过期时间(秒)
                var scriptResult = db.ScriptEvaluate(reentrantLockScript, [lockKey], [lockHolderId, (int)expire.TotalSeconds]);

                // 脚本返回1=成功，0=失败
                return (long)scriptResult == 1;
            });
        }

        /// <summary>
        /// 释放可重入分布式锁
        /// </summary>
        /// <param name="lockKey">锁的Key</param>
        /// <param name="lockHolderId">锁持有者唯一标识（必须与加锁时一致）</param>
        /// <param name="isReentrant">是否启用重入，需和加锁时一致</param>
        /// <returns>true=释放/重入次数减1成功，false=锁不属于当前客户端/锁不存在</returns>
        public bool ReleaseLock(string lockKey, string lockHolderId, bool isReentrant = true)
        {
            if (string.IsNullOrWhiteSpace(lockHolderId))
                return false;

            return ExecuteRedis(lockKey, db =>
            {
                // 非重入锁释放：原子性校验并删除，防止误删
                if (!isReentrant)
                {
                    var normalReleaseScript = @"
                        if redis.call('GET', KEYS[1]) == ARGV[1] then
                            redis.call('DEL', KEYS[1])
                            return 1
                        else
                            return 0
                        end";

                    var result = db.ScriptEvaluate(normalReleaseScript, [lockKey], [lockHolderId]);
                    return (long)result == 1;
                }

                // 可重入锁释放核心Lua脚本（原子性执行减次数/删锁逻辑）
                var reentrantReleaseScript = @"
                    local currentVal = redis.call('GET', KEYS[1])
                    -- 情况1：锁不存在 → 释放失败
                    if currentVal == false then
                        return 0
                    end
                    -- 情况2：锁不属于当前客户端 → 释放失败（防止误删）
                    local clientPart = string.sub(currentVal, 1, -2)
                    if clientPart ~= ARGV[1] then
                        return 0
                    end
                    -- 情况3：重入次数处理
                    local count = tonumber(string.sub(currentVal, -1))
                    if count > 1 then
                        -- 次数>1 → 次数-1，临时续期
                        redis.call('SET', KEYS[1], ARGV[1] .. '_' .. (count-1), 'EX', ARGV[2])
                        return 1
                    else
                        -- 次数=1 → 删除锁，真正释放
                        redis.call('DEL', KEYS[1])
                        return 2
                    end";

                // 执行脚本：ARGV[2]=临时续期时间(秒)
                var scriptResult = db.ScriptEvaluate(reentrantReleaseScript, [lockKey], [lockHolderId, ReentrantLockTempExpireSeconds]);
                // 返回1=次数减1，2=锁删除，均视为成功
                return (long)scriptResult >= 1;
            });
        }

        /// <summary>
        /// 强制释放锁（仅管理员/应急场景使用）
        /// </summary>
        /// <param name="lockKey">锁的唯一标识</param>
        /// <returns>true=释放成功，false=锁不存在</returns>
        public bool ForceReleaseLock(string lockKey)
        {
            var script = "return redis.call('del', KEYS[1])";
            return ExecuteRedis(lockKey, db =>
            {
                var scriptResult = db.ScriptEvaluate(script, [lockKey]);
                return (long)scriptResult > 0;
            });
        }

        /// <summary>
        /// 【异步】获取可重入分布式锁
        /// </summary>
        /// <param name="lockKey">锁的唯一标识</param>
        /// <param name="lockHolderId">锁持有者唯一标识</param>
        /// <param name="expire">锁过期时间</param>
        /// <param name="isReentrant">是否启用重入，默认true</param>
        /// <returns>true=加锁/重入成功，false=加锁失败</returns>
        public async Task<bool> AcquireLockAsync(string lockKey, string lockHolderId, TimeSpan expire, bool isReentrant = true)
        {
            if (string.IsNullOrWhiteSpace(lockHolderId) || expire <= TimeSpan.Zero)
                return false;

            return await ExecuteRedisAsync(lockKey, async db =>
            {
                if (!isReentrant)
                {
                    return await db.StringSetAsync(lockKey, lockHolderId, expire, When.NotExists);
                }

                var reentrantLockScript = @"
                    local currentVal = redis.call('GET', KEYS[1])
                    if currentVal == false then
                        redis.call('SET', KEYS[1], ARGV[1] .. '_1', 'EX', ARGV[2])
                        return 1
                    elseif string.sub(currentVal, 1, -2) == ARGV[1] then
                        local count = tonumber(string.sub(currentVal, -1)) + 1
                        redis.call('SET', KEYS[1], ARGV[1] .. '_' .. count, 'EX', ARGV[2])
                        return 1
                    else
                        return 0
                    end";

                var scriptResult = await db.ScriptEvaluateAsync(reentrantLockScript, [lockKey], [lockHolderId, (int)expire.TotalSeconds]);
                return (long)scriptResult == 1;
            });
        }

        /// <summary>
        /// 释放可重入分布式锁
        /// </summary>
        /// <param name="lockKey">锁的Key</param>
        /// <param name="lockHolderId">加锁时的客户端ID</param>
        /// <param name="isReentrant">是否启用重入，需和加锁时一致</param>
        /// <returns>true=释放/重入次数减1成功，false=锁不属于当前客户端/锁不存在</returns>
        public async Task<bool> ReleaseLockAsync(string lockKey, string lockHolderId, bool isReentrant = true)
        {
            if (string.IsNullOrWhiteSpace(lockHolderId))
                return false;

            return await ExecuteRedisAsync(lockKey, async db =>
            {
                if (!isReentrant)
                {
                    var normalReleaseScript = @"
                        if redis.call('GET', KEYS[1]) == ARGV[1] then
                            redis.call('DEL', KEYS[1])
                            return 1
                        else
                            return 0
                        end";

                    var result = await db.ScriptEvaluateAsync(normalReleaseScript, [lockKey], [lockHolderId]);
                    return (long)result == 1;
                }

                var reentrantReleaseScript = @"
                    local currentVal = redis.call('GET', KEYS[1])
                    if currentVal == false then
                        return 0
                    end
                    local clientPart = string.sub(currentVal, 1, -2)
                    if clientPart ~= ARGV[1] then
                        return 0
                    end
                    local count = tonumber(string.sub(currentVal, -1))
                    if count > 1 then
                        redis.call('SET', KEYS[1], ARGV[1] .. '_' .. (count-1), 'EX', ARGV[2])
                        return 1
                    else
                        redis.call('DEL', KEYS[1])
                        return 2
                    end";

                var scriptResult = await db.ScriptEvaluateAsync(reentrantReleaseScript, [lockKey], [lockHolderId, ReentrantLockTempExpireSeconds]);
                return (long)scriptResult >= 1;
            });
        }


        /// <summary>
        /// 强制释放锁（仅管理员/应急场景使用）
        /// </summary>
        /// <param name="lockKey">锁的唯一标识</param>
        /// <returns>true=释放成功，false=锁不存在</returns>
        public async Task<bool> ForceReleaseLockAsync(string lockKey)
        {
            var script = "return redis.call('del', KEYS[1])";
            return await ExecuteRedisAsync(lockKey, async db =>
            {
                var scriptResult = await db.ScriptEvaluateAsync(script, [lockKey]);
                return (long)scriptResult > 0;
            });
        }

        #endregion
    }
}