using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Viv.Contracts
{
    /// <summary>
    /// 线程安全的运行时静态缓存工具
    /// 核心特性：
    /// 1. 按类型隔离缓存数据，不同类型的数据互不干扰；
    /// 2. 基于ConcurrentDictionary，天然线程安全，支持高并发；
    /// 3. 支持写入、读取、删除、清空缓存操作；
    /// </summary>
    public static class VivConfigRegistry
    {
        private static readonly ConcurrentDictionary<string, object> _cache = new();

        /// <summary>
        /// 写入缓存
        /// </summary>
        /// <typeparam name="T">缓存数据类型</typeparam>
        /// <param name="value">要缓存的数据（允许null）</param>
        public static void Add<T>(T value)
        {
            ArgumentNullException.ThrowIfNull(value);
            var key = GetTypeKey<T>();
            _cache.AddOrUpdate(key, value, (_, _) => value);
        }

        /// <summary>
        /// 从运行时缓存读取泛型数据
        /// </summary>
        /// <typeparam name="T">缓存数据类型</typeparam>
        /// <returns>缓存中的数据，或默认值</returns>
        [return: MaybeNull]
        public static T Get<T>()
        {
            var key = GetTypeKey<T>();
            if (_cache.TryGetValue(key, out var cacheObj) && cacheObj is T cacheItem)
            {
                return cacheItem;
            }

            return default;
        }

        /// <summary>
        /// 删除指定类型的缓存数据
        /// </summary>
        public static bool Remove<T>()
        {
            var key = GetTypeKey<T>();
            return _cache.TryRemove(key, out _);
        }

        /// <summary>
        /// 清空所有运行时缓存
        /// </summary>
        public static void ClearAll()
        {
            _cache.Clear();
        }

        /// <summary>
        /// 获取类型的唯一Key
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <returns>类型的唯一标识Key</returns>
        private static string GetTypeKey<T>()
        {
            return typeof(T).FullName ?? typeof(T).Name;
        }
    }
}