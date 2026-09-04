using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
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
        /// 续期管理：记录每个锁的持有者与取消令牌源，用于停止续期
        /// </summary>
        private static readonly ConcurrentDictionary<string, RenewalEntry> _renewalTasks = new();

        /// <summary>
        /// 锁续期条目：持有者 + 取消令牌。
        /// 记录持有者用于区分「同持有者重入（复用已有续期任务）」与「锁已易主（替换旧任务）」，
        /// 避免续期任务退出时的 TryRemove 误删新持有者的登记。
        /// </summary>
        private sealed class RenewalEntry(string holderId, CancellationTokenSource cts)
        {
            public string HolderId { get; } = holderId;

            public CancellationTokenSource Cts { get; } = cts;
        }

        /// <summary>
        /// 锁续期阈值：在过期时间的一半时进行续期
        /// </summary>
        private const double RenewalThreshold = 0.5;

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
                // 加锁成功，启动续期任务
                StartRenewal(lockKey, lockHolderId, expire);
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
                        StopRenewal(lockKey);
                    return released;
                }

                var scriptResult = db.ScriptEvaluate(RedisLockScripts.ReentrantRelease, [lockKey], [lockHolderId, ReentrantLockTempExpireSeconds]);
                var code = (long)scriptResult;
                // 仅完全释放（count 1→0，DEL）才停续期；仅递减重入计数（count>1）时锁仍持有，续期继续
                if (code == 2)
                    StopRenewal(lockKey);
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
                        StopRenewal(lockKey);
                    return released;
                }

                var scriptResult = await db.ScriptEvaluateAsync(RedisLockScripts.ReentrantRelease, [lockKey], [lockHolderId, ReentrantLockTempExpireSeconds]).ConfigureAwait(false);
                var code = (long)scriptResult;
                // 仅完全释放（count 1→0，DEL）才停续期；仅递减重入计数（count>1）时锁仍持有，续期继续
                if (code == 2)
                    StopRenewal(lockKey);
                return code >= 1;
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// 强制释放锁（仅管理员/应急场景使用）
        /// </summary>
        public bool ForceReleaseLock(string lockKey)
        {
            // 强制释放同样要停掉后台续期任务，否则它会一直空转续查
            StopRenewal(lockKey);

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
            StopRenewal(lockKey);

            var script = "return redis.call('del', KEYS[1])";
            return await ExecuteRedisAsync(lockKey, async db =>
            {
                var scriptResult = await db.ScriptEvaluateAsync(script, [lockKey]).ConfigureAwait(false);
                return (long)scriptResult > 0;
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// 启动锁续期任务（内部方法）
        /// </summary>
        private void StartRenewal(string lockKey, string lockHolderId, TimeSpan expire)
        {
            var entry = new RenewalEntry(lockHolderId, new CancellationTokenSource());
            var cts = entry.Cts;

            // CAS 登记循环：
            // - 无条目 → TryAdd 登记自己；
            // - 同持有者（重入加锁）→ 复用已有任务，不重复启动；
            // - 不同持有者（旧锁已丢失/被接管）→ TryUpdate 原子替换（仅当当前值仍是旧条目时才替换），
            //   替换成功后取消旧任务，保证新持有者必有续期。
            while (true)
            {
                if (!_renewalTasks.TryGetValue(lockKey, out var current))
                {
                    if (_renewalTasks.TryAdd(lockKey, entry))
                        break; // 登记成功
                    continue;   // 竞态：期间有人登记，重读
                }

                if (current.HolderId == lockHolderId)
                {
                    cts.Dispose();
                    return; // 同持有者，复用已有续期任务
                }

                if (_renewalTasks.TryUpdate(lockKey, entry, current))
                {
                    // 原子替换成功，取消旧持有者的续期任务（避免它继续续期已易主的锁）
                    current.Cts.Cancel();
                    current.Cts.Dispose();
                    break;
                }
                // TryUpdate 失败 = 期间条目已变化，重读重试
            }

            // 计算续期间隔：过期时间的一半
            var interval = TimeSpan.FromSeconds(expire.TotalSeconds * RenewalThreshold);
            Task.Run(async () =>
            {
                var token = cts.Token;
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(interval, token).ConfigureAwait(false);
                        if (token.IsCancellationRequested) break;

                        // 执行续期脚本
                        var renewed = await ExecuteRedisAsync(lockKey, async db =>
                        {
                            var result = await db.ScriptEvaluateAsync(RedisLockScripts.Renew, [lockKey], [lockHolderId, (int)expire.TotalSeconds]).ConfigureAwait(false);
                            return (long)result == 1;
                        }).ConfigureAwait(false);

                        // 续期失败 = 锁已丢失（被强制释放 / 被他人覆盖 / 已过期），停止续期，避免无限空转
                        if (!renewed)
                        {
                            break;
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"锁续期失败 Key:{lockKey}, Error:{ex.Message}", ex);
                        // 续期失败时停止续期，避免无限重试
                        break;
                    }
                }
                // 移除任务记录：只移除自己登记的条目（键值都匹配），
                // 避免误删新持有者已登记的续期任务
                _renewalTasks.TryRemove(new KeyValuePair<string, RenewalEntry>(lockKey, entry));
            }, cts.Token);
        }

        /// <summary>
        /// 停止锁续期（内部方法）
        /// </summary>
        private static void StopRenewal(string lockKey)
        {
            if (_renewalTasks.TryRemove(lockKey, out var entry))
            {
                entry.Cts.Cancel();
                entry.Cts.Dispose();
            }
        }

        #endregion
    }
}