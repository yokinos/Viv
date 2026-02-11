using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Viv.Vva.Extension;

namespace Viv.Redis
{
    /// <summary>
    /// Redis 核心操作封装类
    /// 提供同步/异步、单Key/批量Key的Redis操作封装，自动按Key路由到对应Redis数据库（Db），内置异常捕获和日志记录
    /// 继承自 RedisFactory，复用数据库获取、日志记录等基础能力
    /// </summary>
    public class VivRedis : RedisFactory
    {

        /// <summary>
        /// 异步执行单个Key的Redis操作（自动路由到对应Db，内置异常捕获）
        /// </summary>
        /// <typeparam name="T">操作返回值类型</typeparam>
        /// <param name="key">Redis键（用于路由到对应数据库，不能为空）</param>
        /// <param name="func">Redis操作委托，接收IDatabase实例，返回异步操作结果</param>
        /// <returns>
        /// 操作执行结果（执行失败/Key为空时返回默认值）；
        /// 可空类型标注：返回值可能为null/默认值，需结合业务判断有效性
        /// </returns>
        [return: MaybeNull]
        public async Task<T?> ExecuteRedisAsync<T>(string key, Func<IDatabase, Task<T>> func)
        {
            try
            {
                if (key.IsNullOrEmpty()) { return default; }
                var database = await GetDatabaseAsync(key);
                return await func(database);
            }
            catch (Exception ex)
            {
                WriteLog($"Redis操作执行失败: {ex.Message}", ex);
                return default;
            }
        }

        /// <summary>
        /// 同步执行单个Key的Redis操作（自动路由到对应Db，内置异常捕获）
        /// </summary>
        /// <typeparam name="T">操作返回值类型</typeparam>
        /// <param name="key">Redis键（用于路由到对应数据库，不能为空）</param>
        /// <param name="func">Redis操作委托，接收IDatabase实例，返回同步操作结果</param>
        /// <returns>
        /// 操作执行结果（执行失败/Key为空时返回默认值）；
        /// 可空类型标注：返回值可能为null/默认值，需结合业务判断有效性
        /// </returns>
        [return: MaybeNull]
        public T ExecuteRedis<T>(string key, Func<IDatabase, T> func)
        {
            try
            {
                if (key.IsNullOrEmpty()) { return default; }
                var database = Task.Run(async () => await GetDatabaseAsync(key)).Result;
                return func(database);
            }
            catch (Exception ex)
            {
                WriteLog($"Redis操作执行失败: {ex.Message}", ex);
                return default;
            }
        }

        /// <summary>
        /// 异步执行批量Key的Redis操作（按Key路由到对应Db分组执行，内置异常捕获）
        /// </summary>
        /// <typeparam name="T">单个Db分组操作的返回值类型</typeparam>
        /// <param name="keyList">Redis键列表（用于分组路由到对应数据库，空列表返回空结果）</param>
        /// <param name="func">
        /// Redis批量操作委托，接收：
        /// 1. IDatabase：当前分组对应的Redis数据库实例；
        /// 2. RedisKey[]：当前分组下的所有Redis键；
        /// 返回异步操作结果
        /// </param>
        /// <returns>
        /// 各Db分组操作结果的列表（过滤默认值）；
        /// 执行失败/Key列表为空时返回空列表；
        /// 可空类型标注：列表本身不为null，但列表元素可能为默认值（已过滤）
        /// </returns>
        [return: MaybeNull]
        public async Task<List<T>> ExecuteRedisAsync<T>(List<string> keyList, Func<IDatabase, RedisKey[], Task<T>> func)
        {
            try
            {
                if (_dbAllocator is null || keyList.IsNullOrEmpty()) return [];
                var keyDict = _dbAllocator.AllocateGroupDbIndex(keyList, CurrentRedisOptions?.MaxDbIndex);
                var list = new List<T>();

                foreach (var x in keyDict)
                {
                    var database = await GetDatabaseAsync(x.Key);
                    if (database is null)
                    {
                        continue;
                    }
                    var dbResult = await func(database, x.Value);
                    if (!EqualityComparer<T>.Default.Equals(dbResult, default))
                    {
                        list.Add(dbResult);
                    }
                }

                return list;
            }
            catch (Exception ex)
            {
                WriteLog($"Redis操作执行失败: {ex.Message}", ex);
                return [];
            }
        }

        /// <summary>
        /// 同步执行批量Key的Redis操作（按Key路由到对应Db分组执行，内置异常捕获）
        /// </summary>
        /// <typeparam name="T">单个Db分组操作的返回值类型</typeparam>
        /// <param name="keyList">Redis键列表（用于分组路由到对应数据库，空列表返回空结果）</param>
        /// <param name="func">
        /// Redis批量操作委托，接收：
        /// 1. IDatabase：当前分组对应的Redis数据库实例；
        /// 2. RedisKey[]：当前分组下的所有Redis键；
        /// 返回同步操作结果
        /// </param>
        /// <returns>
        /// 各Db分组操作结果的列表（过滤默认值）；
        /// 执行失败/Key列表为空时返回空列表；
        /// 可空类型标注：列表本身不为null，但列表元素可能为默认值（已过滤）
        /// </returns>
        [return: MaybeNull]
        public List<T> ExecuteRedis<T>(List<string> keyList, Func<IDatabase, RedisKey[], T> func)
        {
            try
            {
                if (_dbAllocator is null || keyList.IsNullOrEmpty()) return [];
                var keyDict = _dbAllocator.AllocateGroupDbIndex(keyList, CurrentRedisOptions?.MaxDbIndex);
                var list = new List<T>();

                foreach (var x in keyDict)
                {
                    var database = GetDatabase(x.Key);
                    if (database is null)
                    {
                        continue;
                    }

                    var dbResult = func(database, x.Value);
                    if (!EqualityComparer<T>.Default.Equals(dbResult, default))
                    {
                        list.Add(dbResult);
                    }
                }

                return list;
            }
            catch (Exception ex)
            {
                WriteLog($"Redis操作执行失败: {ex.Message}", ex);
                return [];
            }
        }
    }
}