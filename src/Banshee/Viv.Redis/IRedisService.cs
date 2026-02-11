using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Interface;

namespace Viv.Redis
{
    /// <summary>
    /// Redis 操作服务接口
    /// 封装 Redis 基础操作、Hash操作、List操作、发布订阅、分布式锁等核心功能
    /// </summary>
    public interface IRedisService : IDistributedLock
    {
        /// <summary>
        /// 新增字符串类型缓存（指定过期时间秒数）
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="value">缓存值（会自动序列化为JSON字符串）</param>
        /// <param name="seconds">过期时间（秒），默认600秒</param>
        /// <returns>操作是否成功</returns>
        bool Add(string key, object value, int seconds = 600) => Add(key, value, TimeSpan.FromSeconds(seconds));

        /// <summary>
        /// 新增字符串类型缓存（指定过期时间TimeSpan）
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="value">缓存值（会自动序列化为JSON字符串）</param>
        /// <param name="expire">过期时间</param>
        /// <returns>操作是否成功</returns>
        bool Add(string key, object value, TimeSpan expire);

        /// <summary>
        /// 异步新增字符串类型缓存（指定过期时间秒数）
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="value">缓存值（会自动序列化为JSON字符串）</param>
        /// <param name="seconds">过期时间（秒），默认600秒</param>
        /// <returns>操作是否成功</returns>
        Task<bool> AddAsync(string key, object value, int seconds = 600) => AddAsync(key, value, TimeSpan.FromSeconds(seconds));

        /// <summary>
        /// 异步新增字符串类型缓存（指定过期时间TimeSpan）
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="value">缓存值（会自动序列化为JSON字符串）</param>
        /// <param name="expire">过期时间</param>
        /// <returns>操作是否成功</returns>
        Task<bool> AddAsync(string key, object value, TimeSpan expire);

        /// <summary>
        /// 延迟缓存过期时间
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="delayTime">需要延迟的时长</param>
        /// <returns>操作是否成功（键不存在/无过期时间时返回false）</returns>
        bool DelayExpire(string key, TimeSpan delayTime);

        /// <summary>
        /// 异步延迟缓存过期时间
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="delayTime">需要延迟的时长</param>
        /// <returns>操作是否成功（键不存在/无过期时间时返回false）</returns>
        Task<bool> DelayExpireAsync(string key, TimeSpan delayTime);

        /// <summary>
        /// 检查指定键是否存在
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <returns>存在返回true，否则返回false</returns>
        bool Exist(string key);

        /// <summary>
        /// 异步检查指定键是否存在
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <returns>存在返回true，否则返回false</returns>
        Task<bool> ExistAsync(string key);

        /// <summary>
        /// 获取字符串类型缓存值（未反序列化）
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <returns>原始缓存值（键不存在返回null）</returns>
        object Get(string key);

        /// <summary>
        /// 获取字符串类型缓存值并反序列化为指定类型
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="key">Redis键</param>
        /// <returns>反序列化后的对象（键不存在返回默认值）</returns>
        T Get<T>(string key);

        /// <summary>
        /// 异步获取字符串类型缓存值（未反序列化）
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <returns>原始缓存值（键不存在返回null）</returns>
        Task<object> GetAsync(string key);

        /// <summary>
        /// 异步获取字符串类型缓存值并反序列化为指定类型
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="key">Redis键</param>
        /// <returns>反序列化后的对象（键不存在返回默认值）</returns>
        Task<T> GetAsync<T>(string key);

        /// <summary>
        /// 发布消息到指定频道
        /// </summary>
        /// <param name="channel">Redis频道</param>
        /// <param name="message">消息内容（会自动序列化为JSON字符串）</param>
        /// <returns>接收消息的客户端数量</returns>
        long Publish(RedisChannel channel, object message);

        /// <summary>
        /// 异步发布消息到指定频道
        /// </summary>
        /// <param name="channel">Redis频道</param>
        /// <param name="message">消息内容（会自动序列化为JSON字符串）</param>
        /// <returns>接收消息的客户端数量</returns>
        Task<long> PublishAsync(RedisChannel channel, object message);

        /// <summary>
        /// 批量删除指定的Redis键
        /// </summary>
        /// <param name="keyList">需要删除的键列表</param>
        /// <returns>成功删除的键数量</returns>
        long Remove(List<string> keyList);

        /// <summary>
        /// 删除单个Redis键
        /// </summary>
        /// <param name="key">需要删除的键</param>
        /// <returns>是否删除成功（键不存在返回false）</returns>
        bool Remove(string key);

        /// <summary>
        /// 异步批量删除指定的Redis键
        /// </summary>
        /// <param name="keyList">需要删除的键列表</param>
        /// <returns>成功删除的键数量</returns>
        Task<long> RemoveAsync(List<string> keyList);

        /// <summary>
        /// 异步删除单个Redis键
        /// </summary>
        /// <param name="key">需要删除的键</param>
        /// <returns>是否删除成功（键不存在返回false）</returns>
        Task<bool> RemoveAsync(string key);

        /// <summary>
        /// 设置键的过期时间
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="expire">过期时间</param>
        /// <returns>操作是否成功</returns>
        bool SetKeyExpire(string key, TimeSpan expire);

        /// <summary>
        /// 异步设置键的过期时间
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="expire">过期时间</param>
        /// <returns>操作是否成功</returns>
        Task<bool> SetKeyExpireAsync(string key, TimeSpan expire);

        /// <summary>
        /// 订阅指定频道的消息
        /// </summary>
        /// <typeparam name="T">消息反序列化目标类型</typeparam>
        /// <param name="channel">Redis频道</param>
        /// <param name="action">消息接收后的处理回调</param>
        void Subscribe<T>(RedisChannel channel, Action<T> action);

        /// <summary>
        /// 异步订阅指定频道的消息
        /// </summary>
        /// <typeparam name="T">消息反序列化目标类型</typeparam>
        /// <param name="channel">Redis频道</param>
        /// <param name="action">消息接收后的处理回调</param>
        /// <returns>异步任务</returns>
        Task SubscribeAsync<T>(RedisChannel channel, Action<T> action);

        /// <summary>
        /// 取消订阅指定频道
        /// </summary>
        /// <param name="channel">Redis频道</param>
        void Unsubscribe(RedisChannel channel);

        /// <summary>
        /// 取消所有频道的订阅
        /// </summary>
        void UnsubscribeAll();

        /// <summary>
        /// 异步取消所有频道的订阅
        /// </summary>
        /// <returns>异步任务</returns>
        Task UnsubscribeAllAsync();

        /// <summary>
        /// 异步取消订阅指定频道
        /// </summary>
        /// <param name="channel">Redis频道</param>
        /// <returns>异步任务</returns>
        Task UnsubscribeAsync(RedisChannel channel);

        /// <summary>
        /// 设置Hash类型缓存的字段值
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="field">Hash字段名</param>
        /// <param name="value">字段值（会自动序列化为JSON字符串）</param>
        /// <returns>操作是否成功</returns>
        bool HashSet(string key, string field, object value);

        /// <summary>
        /// 异步设置Hash类型缓存的字段值
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="field">Hash字段名</param>
        /// <param name="value">字段值（会自动序列化为JSON字符串）</param>
        /// <returns>操作是否成功</returns>
        Task<bool> HashSetAsync(string key, string field, object value);

        /// <summary>
        /// 获取Hash类型缓存的指定字段值并反序列化为指定类型
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="key">Redis键</param>
        /// <param name="field">Hash字段名</param>
        /// <returns>反序列化后的字段值（字段不存在返回默认值）</returns>
        T HashGet<T>(string key, string field);

        /// <summary>
        /// 异步获取Hash类型缓存的指定字段值并反序列化为指定类型
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="key">Redis键</param>
        /// <param name="field">Hash字段名</param>
        /// <returns>反序列化后的字段值（字段不存在返回默认值）</returns>
        Task<T> HashGetAsync<T>(string key, string field);

        /// <summary>
        /// 检查Hash类型缓存的指定字段是否存在
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="field">Hash字段名</param>
        /// <returns>存在返回true，否则返回false</returns>
        bool HashExist(string key, string field);

        /// <summary>
        /// 异步检查Hash类型缓存的指定字段是否存在
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="field">Hash字段名</param>
        /// <returns>存在返回true，否则返回false</returns>
        Task<bool> HashExistAsync(string key, string field);

        /// <summary>
        /// 删除Hash类型缓存的一个或多个字段
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="fields">需要删除的字段名数组</param>
        /// <returns>成功删除的字段数量</returns>
        long HashRemove(string key, params string[] fields);

        /// <summary>
        /// 异步删除Hash类型缓存的一个或多个字段
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="fields">需要删除的字段名数组</param>
        /// <returns>成功删除的字段数量</returns>
        Task<long> HashRemoveAsync(string key, params string[] fields);

        /// <summary>
        /// 获取Hash类型缓存的所有字段和值，并反序列化为指定类型的字典
        /// </summary>
        /// <typeparam name="T">值的目标类型</typeparam>
        /// <param name="key">Redis键</param>
        /// <returns>字段名-值字典（键不存在返回空字典）</returns>
        Dictionary<string, T> HashGetAll<T>(string key);

        /// <summary>
        /// 异步获取Hash类型缓存的所有字段和值，并反序列化为指定类型的字典
        /// </summary>
        /// <typeparam name="T">值的目标类型</typeparam>
        /// <param name="key">Redis键</param>
        /// <returns>字段名-值字典（键不存在返回空字典）</returns>
        Task<Dictionary<string, T>> HashGetAllAsync<T>(string key);

        /// <summary>
        /// 向List类型缓存的尾部（右侧）添加元素
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="value">元素值（会自动序列化为JSON字符串）</param>
        /// <returns>添加元素后列表的长度</returns>
        long ListPush(string key, object value);

        /// <summary>
        /// 异步向List类型缓存的尾部（右侧）添加元素
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="value">元素值（会自动序列化为JSON字符串）</param>
        /// <returns>添加元素后列表的长度</returns>
        Task<long> ListPushAsync(string key, object value);

        /// <summary>
        /// 从List类型缓存的尾部（右侧）弹出一个元素
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <returns>弹出的原始元素值（列表为空返回null）</returns>
        object ListPop(string key);

        /// <summary>
        /// 异步从List类型缓存的尾部（右侧）弹出一个元素
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <returns>弹出的原始元素值（列表为空返回null）</returns>
        Task<object> ListPopAsync(string key);

        /// <summary>
        /// 获取List类型缓存中指定范围的元素并反序列化为指定类型
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="key">Redis键</param>
        /// <param name="start">起始索引（0表示第一个元素）</param>
        /// <param name="stop">结束索引（-1表示最后一个元素）</param>
        /// <returns>指定范围的元素列表（键不存在返回空列表）</returns>
        List<T> ListRange<T>(string key, long start = 0, long stop = -1);

        /// <summary>
        /// 异步获取List类型缓存中指定范围的元素并反序列化为指定类型
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="key">Redis键</param>
        /// <param name="start">起始索引（0表示第一个元素）</param>
        /// <param name="stop">结束索引（-1表示最后一个元素）</param>
        /// <returns>指定范围的元素列表（键不存在返回空列表）</returns>
        Task<List<T>> ListRangeAsync<T>(string key, long start = 0, long stop = -1);

        /// <summary>
        /// 从List类型缓存中删除指定数量的匹配元素
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="value">需要删除的元素值（会自动序列化为JSON字符串）</param>
        /// <param name="count">删除数量：0=删除所有匹配项；正数=删除前N个；负数=删除后N个</param>
        /// <returns>成功删除的元素数量</returns>
        long ListRemove(string key, object value, long count = 0);

        /// <summary>
        /// 异步从List类型缓存中删除指定数量的匹配元素
        /// </summary>
        /// <param name="key">Redis键</param>
        /// <param name="value">需要删除的元素值（会自动序列化为JSON字符串）</param>
        /// <param name="count">删除数量：0=删除所有匹配项；正数=删除前N个；负数=删除后N个</param>
        /// <returns>成功删除的元素数量</returns>
        Task<long> ListRemoveAsync(string key, object value, long count = 0);

        /// <summary>
        /// 获取分布式锁
        /// </summary>
        /// <param name="lockKey">锁的唯一标识（如：stock_lock_1001）</param>
        /// <param name="lockHolderId">锁持有者唯一标识</param>
        /// <param name="expire">锁过期时间（必须>0，防止死锁）</param>
        /// <param name="isReentrant">是否启用重入，默认true</param>
        /// <returns>true=加锁/重入成功，false=加锁失败</returns>
        bool AcquireLock(string lockKey, string lockHolderId, TimeSpan expire, bool isReentrant = true);

        /// <summary>
        /// 释放分布式锁
        /// </summary>
        /// <param name="lockKey">锁的唯一标识</param>
        /// <param name="lockHolderId">锁持有者唯一标识（必须与加锁时一致）</param>
        /// <param name="enableReentrant">是否启用重入，需和加锁时一致</param>
        /// <returns>true=释放/重入次数减1成功，false=锁不属于当前持有者/锁不存在</returns>
        bool ReleaseLock(string lockKey, string lockHolderId, bool enableReentrant = true);

        /// <summary>
        /// 强制释放锁（仅管理员/应急场景使用）
        /// </summary>
        /// <param name="lockKey">锁的唯一标识</param>
        /// <returns>true=释放成功，false=锁不存在</returns>
        bool ForceReleaseLock(string lockKey);

        /// <summary>
        /// 获取分布式锁
        /// </summary>
        /// <param name="lockKey">锁的唯一标识</param>
        /// <param name="lockHolderId">锁持有者唯一标识</param>
        /// <param name="expire">锁过期时间</param>
        /// <param name="isReentrant">是否启用重入，默认true</param>
        /// <returns>true=加锁/重入成功，false=加锁失败</returns>
        Task<bool> AcquireLockAsync(string lockKey, string lockHolderId, TimeSpan expire, bool isReentrant = true);

        /// <summary>
        /// 释放分布式锁
        /// </summary>
        /// <param name="lockKey">锁的唯一标识</param>
        /// <param name="lockHolderId">锁持有者唯一标识</param>
        /// <param name="isReentrant">是否启用重入，需和加锁时一致</param>
        /// <returns>true=释放/重入次数减1成功，false=锁不属于当前持有者/锁不存在</returns>
        Task<bool> ReleaseLockAsync(string lockKey, string lockHolderId, bool isReentrant = true);

        /// <summary>
        /// 强制释放锁（仅管理员/应急场景使用）
        /// </summary>
        /// <param name="lockKey">锁的唯一标识</param>
        /// <returns>true=释放成功，false=锁不存在</returns>
        Task<bool> ForceReleaseLockAsync(string lockKey);
    }
}