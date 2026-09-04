using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Viv.Contracts;
using Viv.Contracts.Exceptions;
using Viv.Delusion.Extension;

#nullable disable
namespace Viv.Redis
{
    /// <summary>
    /// Redis 业务操作实现类
    /// 继承 VivRedis 复用连接管理和通用执行逻辑，实现 IRedisService 接口规范
    /// 核心特性：
    /// 1. 自动序列化/反序列化（ToJson/As）；
    /// 2. 内置空值校验、异常捕获；
    /// 3. 支持同步/异步双版本方法；
    /// 4. 分布式锁基于Lua脚本保证原子性，并支持自动续期；
    /// 5. 批量操作按Db分组执行，提升性能。
    /// </summary>
    public class RedisService : VivRedis, IRedisService
    {
        /// <summary>
        /// 重入锁释放时的临时续期时间（秒），防止释放过程中锁过期
        /// </summary>
        private const int ReentrantLockTempExpireSeconds = 60;

        public RedisService() { }

        /// <summary>
        /// 反序列化 RedisValue → T。
        /// 写侧统一 value.ToJson()（string 存原文、其他类型存 JSON 文本），
        /// 读侧必须先取文本（value.ToString()）再 As&lt;T&gt;()：直接 value.As&lt;T&gt;() 会让
        /// ObjectMapper.TryConvert 对 RedisValue 结构体调用 JsonConvert.SerializeObject，得到垃圾数据。
        /// string 目标由 TryConvert 的 source is T 直接命中，复杂类型落到 DeserializeObject(string.ToJson())，都正确。
        /// </summary>
        [return: MaybeNull]
        private static T ReadRedis<T>(RedisValue value)
        {
            return value.ToString().As<T>();
        }

        #region 基础字符串操作

        public bool Add(string key, object value, TimeSpan expire)
        {
            if (value == null) return false;
            return ExecuteRedis(key, x => x.StringSet(key, value.ToJson(), expire));
        }

        public async Task<bool> AddAsync(string key, object value, TimeSpan expire)
        {
            if (value == null) return false;
            return await ExecuteRedisAsync(key, async x => await x.StringSetAsync(key, value.ToJson(), expire).ConfigureAwait(false)).ConfigureAwait(false);
        }

        public bool Exist(string key)
        {
            return ExecuteRedis(key, x => x.KeyExists(key));
        }

        public async Task<bool> ExistAsync(string key)
        {
            return await ExecuteRedisAsync(key, async x => await x.KeyExistsAsync(key).ConfigureAwait(false)).ConfigureAwait(false);
        }

        public object Get(string key)
        {
            return ExecuteRedis(key, x => x.StringGet(key));
        }

        [return: MaybeNull]
        public T Get<T>(string key)
        {
            return ExecuteRedis(key, x =>
            {
                var value = x.StringGet(key);
                if (value.IsNull) return default;
                return ReadRedis<T>(value);
            });
        }

        public async Task<object> GetAsync(string key)
        {
            return await ExecuteRedisAsync(key, async x => await x.StringGetAsync(key).ConfigureAwait(false)).ConfigureAwait(false);
        }

        public async Task<T> GetAsync<T>(string key)
        {
            return await ExecuteRedisAsync(key, async x =>
            {
                var value = await x.StringGetAsync(key).ConfigureAwait(false);
                if (value.IsNull) return default;
                return ReadRedis<T>(value);
            }).ConfigureAwait(false);
        }

        #endregion

        #region 过期时间操作

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

        public async Task<bool> DelayExpireAsync(string key, TimeSpan delayTime)
        {
            return await ExecuteRedisAsync(key, async x =>
            {
                var expire = await x.KeyExpireTimeAsync(key).ConfigureAwait(false);
                if (expire != null)
                {
                    return await x.KeyExpireAsync(key, expire.Value + delayTime).ConfigureAwait(false);
                }
                return false;
            }).ConfigureAwait(false);
        }

        public bool SetKeyExpire(string key, TimeSpan expire)
        {
            if (expire <= TimeSpan.Zero) return false;
            return ExecuteRedis(key, x => x.KeyExpire(key, expire));
        }

        public async Task<bool> SetKeyExpireAsync(string key, TimeSpan expire)
        {
            if (expire <= TimeSpan.Zero) return false;
            return await ExecuteRedisAsync(key, async x => await x.KeyExpireAsync(key, expire).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion

        #region 删除操作

        public bool Remove(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            return ExecuteRedis(key, x => x.KeyDelete(key));
        }

        public long Remove(List<string> keyList)
        {
            var list = ExecuteRedis(keyList, (x, keys) => x.KeyDelete(keys));
            return list.Sum();
        }

        public async Task<bool> RemoveAsync(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            return await ExecuteRedisAsync(key, async x => await x.KeyDeleteAsync(key).ConfigureAwait(false)).ConfigureAwait(false);
        }

        public async Task<long> RemoveAsync(List<string> keyList)
        {
            var list = await ExecuteRedisAsync(keyList, async (x, keys) => await x.KeyDeleteAsync(keys).ConfigureAwait(false)).ConfigureAwait(false);
            return list.Sum();
        }

        #endregion

        #region Hash操作

        public bool HashExist(string key, string field)
        {
            return ExecuteRedis(key, x => x.HashExists(key, field));
        }

        public async Task<bool> HashExistAsync(string key, string field)
        {
            return await ExecuteRedisAsync(key, async x => await x.HashExistsAsync(key, field).ConfigureAwait(false)).ConfigureAwait(false);
        }

        public T HashGet<T>(string key, string field)
        {
            return ExecuteRedis(key, x =>
            {
                var hashValue = x.HashGet(key, field);
                if (hashValue.IsNull) return default;
                return ReadRedis<T>(hashValue);
            });
        }

        public async Task<T> HashGetAsync<T>(string key, string field)
        {
            return await ExecuteRedisAsync(key, async x =>
            {
                var hashValue = await x.HashGetAsync(key, field).ConfigureAwait(false);
                if (hashValue.IsNull) return default;
                return ReadRedis<T>(hashValue);
            }).ConfigureAwait(false);
        }

        public Dictionary<string, T> HashGetAll<T>(string key)
        {
            return ExecuteRedis(key, x => x.HashGetAll(key).ToDictionary(x => x.Name.ToString(), x => ReadRedis<T>(x.Value)));
        }

        public async Task<Dictionary<string, T>> HashGetAllAsync<T>(string key)
        {
            return await ExecuteRedisAsync(key, async x =>
            {
                var entries = await x.HashGetAllAsync(key).ConfigureAwait(false);
                return entries.ToDictionary(e => e.Name.ToString(), e => ReadRedis<T>(e.Value));
            }).ConfigureAwait(false);
        }

        public bool HashSet(string key, string field, object value)
        {
            if (value == null) return false;
            var redisValue = value.ToJson();
            return ExecuteRedis(key, x => x.HashSet(key, field, redisValue));
        }

        public async Task<bool> HashSetAsync(string key, string field, object value)
        {
            if (value == null) return false;
            var redisValue = value.ToJson();
            return await ExecuteRedisAsync(key, async x => await x.HashSetAsync(key, field, redisValue).ConfigureAwait(false)).ConfigureAwait(false);
        }

        public long HashRemove(string key, params string[] fields)
        {
            if (fields == null || fields.Length == 0) return 0;
            var redisFields = Array.ConvertAll(fields, f => (RedisValue)f);
            return ExecuteRedis(key, x => x.HashDelete(key, redisFields));
        }

        public async Task<long> HashRemoveAsync(string key, params string[] fields)
        {
            if (fields == null || fields.Length == 0) return 0;
            var redisFields = Array.ConvertAll(fields, f => (RedisValue)f);
            return await ExecuteRedisAsync(key, async x => await x.HashDeleteAsync(key, redisFields).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion

        #region List操作

        public long ListPush(string key, object value)
        {
            if (value == null) return 0;
            var redisValue = value.ToJson();
            return ExecuteRedis(key, x => x.ListRightPush(key, redisValue));
        }

        public async Task<long> ListPushAsync(string key, object value)
        {
            if (value == null) return 0;
            var redisValue = value.ToJson();
            return await ExecuteRedisAsync(key, async x => await x.ListRightPushAsync(key, redisValue).ConfigureAwait(false)).ConfigureAwait(false);
        }

        public object ListPop(string key)
        {
            return ExecuteRedis(key, x =>
            {
                var value = x.ListRightPop(key);
                return value.IsNull ? default : value;
            });
        }

        public async Task<object> ListPopAsync(string key)
        {
            return await ExecuteRedisAsync(key, async x =>
            {
                var value = await x.ListRightPopAsync(key).ConfigureAwait(false);
                return value.IsNull ? default : value;
            }).ConfigureAwait(false);
        }

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
                        result.Add(ReadRedis<T>(value));
                    }
                }
                return result;
            });
        }

        public async Task<List<T>> ListRangeAsync<T>(string key, long start = 0, long stop = -1)
        {
            return await ExecuteRedisAsync(key, async x =>
            {
                var values = await x.ListRangeAsync(key, start, stop).ConfigureAwait(false);
                var result = new List<T>();
                foreach (var value in values)
                {
                    if (!value.IsNull)
                    {
                        result.Add(ReadRedis<T>(value));
                    }
                }
                return result;
            }).ConfigureAwait(false);
        }

        public long ListRemove(string key, object value, long count = 0)
        {
            if (value == null) return 0;
            var redisValue = value.ToJson();
            return ExecuteRedis(key, x => x.ListRemove(key, redisValue, count));
        }

        public async Task<long> ListRemoveAsync(string key, object value, long count = 0)
        {
            if (value == null) return 0;
            var redisValue = value.ToJson();
            return await ExecuteRedisAsync(key, async x => await x.ListRemoveAsync(key, redisValue, count).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion

        #region 发布订阅

        public long Publish(RedisChannel channel, object message)
        {
            if (message == null) return 0;
            var redisMessage = message.ToJson();
            return ExecuteRedis(channel, x => x.Publish(channel, redisMessage));
        }

        public async Task<long> PublishAsync(RedisChannel channel, object message)
        {
            if (message == null) return 0;
            var redisMessage = message.ToJson();
            return await ExecuteRedisAsync(channel, async x => await x.PublishAsync(channel, redisMessage).ConfigureAwait(false)).ConfigureAwait(false);
        }

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
                        var data = ReadRedis<T>(msg);
                        action.Invoke(data);
                    }
                });
                return true;
            });
        }

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
                        var data = ReadRedis<T>(msg);
                        action.Invoke(data);
                    }
                }).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
        }

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

        public void UnsubscribeAll()
        {
            ExecuteRedis(string.Empty, x =>
            {
                var subscriber = x.Multiplexer.GetSubscriber();
                subscriber.UnsubscribeAll();
                return true;
            });
        }

        public async Task UnsubscribeAsync(RedisChannel channel)
        {
            if (string.IsNullOrEmpty(channel)) return;
            await ExecuteRedisAsync(channel, async x =>
            {
                var subscriber = x.Multiplexer.GetSubscriber();
                await subscriber.UnsubscribeAsync(channel).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
        }

        public async Task UnsubscribeAllAsync()
        {
            await ExecuteRedisAsync(string.Empty, async x =>
            {
                var subscriber = x.Multiplexer.GetSubscriber();
                await subscriber.UnsubscribeAllAsync().ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
        }

        #endregion

        #region 分布式锁（带自动续期）

        /// <summary>
        /// 获取可重入分布式锁（同步，无续期，建议使用异步版本）
        /// </summary>
        public bool AcquireLock(string lockKey, TimeSpan expire, string lockHolderId = null, bool isReentrant = true)
        {
            if (lockHolderId.IsNullOrEmpty())
                lockHolderId = LockHolderContext.CurrentHolderId;

            if (string.IsNullOrWhiteSpace(lockHolderId) || expire <= TimeSpan.Zero)
                return false;

            // 同步锁不支持续期（续期需要后台线程，同步场景易死锁）
            // 取锁失败统一返回 false，由调用方通过 IsLockHeldAsync 二次裁决（真竞争 vs 服务故障）
            return ExecuteRedis(lockKey, db =>
            {
                if (!isReentrant)
                {
                    return db.StringSet(lockKey, lockHolderId, expire, When.NotExists);
                }

                var scriptResult = db.ScriptEvaluate(RedisLockScripts.ReentrantAcquire, [lockKey], [lockHolderId, (int)expire.TotalSeconds]);
                return (long)scriptResult == 1;
            });
        }

        /// <summary>
        /// 【异步】获取可重入分布式锁（带自动续期）
        /// </summary>
        public async Task<bool> AcquireLockAsync(string lockKey, TimeSpan expire, string lockHolderId = null, bool isReentrant = true)
        {
            if (lockHolderId.IsNullOrEmpty())
                lockHolderId = LockHolderContext.CurrentHolderId;

            if (string.IsNullOrWhiteSpace(lockHolderId) || expire <= TimeSpan.Zero)
                return false;

            var success = await ExecuteRedisAsync(lockKey, async db =>
            {
                if (!isReentrant)
                {
                    return await db.StringSetAsync(lockKey, lockHolderId, expire, When.NotExists).ConfigureAwait(false);
                }

                var scriptResult = await db.ScriptEvaluateAsync(RedisLockScripts.ReentrantAcquire, [lockKey], [lockHolderId, (int)expire.TotalSeconds]).ConfigureAwait(false);
                return (long)scriptResult == 1;
            }).ConfigureAwait(false);

            if (success)
            {
                LockAutoRenewal.Start(lockKey, lockHolderId, expire, () => TryRenewAsync(lockKey, lockHolderId, expire));
            }

            return success;
        }

        /// <summary>
        /// 查询锁当前是否被持有（取锁失败后用于区分「真竞争」与「服务瞬时不稳/故障」）
        /// </summary>
        /// <param name="lockKey">锁的唯一标识</param>
        /// <returns>
        /// <c>true</c> = 锁确实被其他持有者占用（真竞争）；
        /// <c>false</c> = 锁未被持有（说明刚才取锁失败是瞬时不稳/命令异常）
        /// </returns>
        /// <exception cref="DistributedLockException">Redis 不可用、无法确认锁状态时抛出</exception>
        public async Task<bool> IsLockHeldAsync(string lockKey)
        {
            try
            {
                return await ExecuteRedisAsync(lockKey, async db => await db.KeyExistsAsync(lockKey).ConfigureAwait(false)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new DistributedLockException(lockKey, 0, ex);
            }
        }

        /// <summary>
        /// 释放可重入分布式锁（同步）
        /// </summary>
        public bool ReleaseLock(string lockKey, string lockHolderId = null, bool isReentrant = true)
        {
            if (lockHolderId.IsNullOrEmpty())
                lockHolderId = LockHolderContext.CurrentHolderId;

            if (string.IsNullOrWhiteSpace(lockHolderId))
                return false;

            return ExecuteRedis(lockKey, db =>
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

                    var result = db.ScriptEvaluate(normalReleaseScript, [lockKey], [lockHolderId]);
                    var released = (long)result == 1;
                    // 非重入：释放即完全释放，停续期
                    if (released)
                        LockAutoRenewal.Stop(lockKey);
                    return released;
                }

                var scriptResult = db.ScriptEvaluate(RedisLockScripts.ReentrantRelease, [lockKey], [lockHolderId, ReentrantLockTempExpireSeconds]);
                var code = (long)scriptResult;
                // 仅完全释放（count 1→0，DEL）才停续期；仅递减重入计数（count>1）时锁仍持有，续期继续
                if (code == 2)
                    LockAutoRenewal.Stop(lockKey);
                return code >= 1;
            });
        }

        /// <summary>
        /// 释放可重入分布式锁（异步，自动停止续期）
        /// </summary>
        public async Task<bool> ReleaseLockAsync(string lockKey, string lockHolderId = null, bool isReentrant = true)
        {
            if (lockHolderId.IsNullOrEmpty())
                lockHolderId = LockHolderContext.CurrentHolderId;

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

                    var res = await db.ScriptEvaluateAsync(normalReleaseScript, [lockKey], [lockHolderId]).ConfigureAwait(false);
                    var released = (long)res == 1;
                    // 非重入：释放即完全释放，停续期
                    if (released)
                        LockAutoRenewal.Stop(lockKey);
                    return released;
                }

                var scriptResult = await db.ScriptEvaluateAsync(RedisLockScripts.ReentrantRelease, [lockKey], [lockHolderId, ReentrantLockTempExpireSeconds]).ConfigureAwait(false);
                var code = (long)scriptResult;
                // 仅完全释放（count 1→0，DEL）才停续期；仅递减重入计数（count>1）时锁仍持有，续期继续
                if (code == 2)
                    LockAutoRenewal.Stop(lockKey);
                return code >= 1;
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// 强制释放锁（仅管理员/应急场景使用）
        /// </summary>
        public bool ForceReleaseLock(string lockKey)
        {
            // 强制释放同样要停掉后台续期任务，否则它会一直空转续查
            LockAutoRenewal.Stop(lockKey);

            var script = "return redis.call('del', KEYS[1])";
            return ExecuteRedis(lockKey, db =>
            {
                var scriptResult = db.ScriptEvaluate(script, [lockKey]);
                return (long)scriptResult > 0;
            });
        }

        /// <summary>
        /// 强制释放锁（异步，应急使用）
        /// </summary>
        public async Task<bool> ForceReleaseLockAsync(string lockKey)
        {
            // 强制释放同样要停掉后台续期任务，否则它会一直空转续查
            LockAutoRenewal.Stop(lockKey);

            var script = "return redis.call('del', KEYS[1])";
            return await ExecuteRedisAsync(lockKey, async db =>
            {
                var scriptResult = await db.ScriptEvaluateAsync(script, [lockKey]).ConfigureAwait(false);
                return (long)scriptResult > 0;
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// 执行一次续期 Lua。失败记日志并返回 false，由 <see cref="LockAutoRenewal"/> 停转。
        /// </summary>
        private async Task<bool> TryRenewAsync(string lockKey, string lockHolderId, TimeSpan expire)
        {
            try
            {
                return await ExecuteRedisAsync(lockKey, async db =>
                {
                    var result = await db.ScriptEvaluateAsync(
                        RedisLockScripts.Renew,
                        [lockKey],
                        [lockHolderId, (int)expire.TotalSeconds]).ConfigureAwait(false);
                    return (long)result == 1;
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                WriteLog($"锁续期失败 Key:{lockKey}, Error:{ex.Message}", ex);
                return false;
            }
        }

        #endregion
    }
}