using StackExchange.Redis;
using System.Diagnostics.CodeAnalysis;
using Viv.Contracts.Enums;
using Viv.Contracts.Exceptions;
using Viv.Delusion.Extension;

namespace Viv.Redis
{
    /// <summary>
    /// Redis 核心操作封装类
    /// 提供同步/异步、单Key/批量Key的Redis操作封装，自动按Key路由到对应Redis数据库（Db）。
    /// Redis 访问失败抛 <see cref="VivConnectionException"/>，不再吞成 default。
    /// 继承自 RedisFactory，复用数据库获取、日志记录等基础能力
    /// </summary>
    public class VivRedis : RedisFactory
    {
        public VivRedis() { }

        /// <summary>
        /// 异步执行单个Key的Redis操作（自动路由到对应Db）
        /// </summary>
        [return: MaybeNull]
        public async Task<T?> ExecuteRedisAsync<T>(string key, Func<IDatabase, Task<T>> func)
        {
            try
            {
                if (key.IsNullOrEmpty()) { return default; }
                var database = await GetDatabaseAsync(key).ConfigureAwait(false);
                return await func(database).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw WrapRedisException($"Redis操作执行失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 同步执行单个Key的Redis操作（自动路由到对应Db）
        /// </summary>
        [return: MaybeNull]
        public T ExecuteRedis<T>(string key, Func<IDatabase, T> func)
        {
            try
            {
                if (key.IsNullOrEmpty()) { return default; }
                var database = GetDatabaseAsync(key).GetAwaiter().GetResult();
                return func(database);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw WrapRedisException($"Redis操作执行失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 异步执行批量Key的Redis操作（按Key路由到对应Db分组执行）
        /// </summary>
        public async Task<List<T>> ExecuteRedisAsync<T>(List<string> keyList, Func<IDatabase, RedisKey[], Task<T>> func)
        {
            try
            {
                if (_dbAllocator is null || keyList.IsNullOrEmpty()) return [];
                var keyDict = _dbAllocator.AllocateGroupDbIndex(keyList, CurrentRedisOptions?.MaxDbIndex);
                var list = new List<T>();

                foreach (var x in keyDict)
                {
                    var database = await GetDatabaseAsync(x.Key).ConfigureAwait(false);
                    if (database is null)
                    {
                        continue;
                    }
                    var dbResult = await func(database, x.Value).ConfigureAwait(false);
                    if (!EqualityComparer<T>.Default.Equals(dbResult, default))
                    {
                        list.Add(dbResult);
                    }
                }

                return list;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw WrapRedisException($"Redis操作执行失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 同步执行批量Key的Redis操作（按Key路由到对应Db分组执行）
        /// </summary>
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
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw WrapRedisException($"Redis操作执行失败: {ex.Message}", ex);
            }
        }

        private static VivConnectionException WrapRedisException(string message, Exception ex)
        {
            WriteLog(message, ex);
            return new VivConnectionException(VivConnType.Redis, message, ex);
        }
    }
}
