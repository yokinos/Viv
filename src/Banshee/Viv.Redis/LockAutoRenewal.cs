using System.Collections.Concurrent;

namespace Viv.Redis;

/// <summary>
/// 分布式锁自动续期调度：按 lockKey 登记后台循环，与 <see cref="RedisService"/> 解耦。
/// 续期怎么打 Redis 由调用方传入；本类只负责 CAS 登记、同持有者复用、易主替换、失败停转。
/// </summary>
internal static class LockAutoRenewal
{
    /// <summary>在过期时间的一半时续期一次。</summary>
    private const double RenewalThreshold = 0.5;

    private static readonly ConcurrentDictionary<string, Entry> Tasks = new();

    /// <summary>
    /// 锁续期条目：持有者 + 取消令牌。
    /// 记录持有者用于区分「同持有者重入（复用已有续期任务）」与「锁已易主（替换旧任务）」，
    /// 避免续期任务退出时的 TryRemove 误删新持有者的登记。
    /// </summary>
    private sealed class Entry(string holderId, CancellationTokenSource cts)
    {
        public string HolderId { get; } = holderId;

        public CancellationTokenSource Cts { get; } = cts;
    }

    /// <summary>
    /// 启动（或复用）指定锁的续期循环。<paramref name="tryRenew"/> 返回 false 或抛异常则停止，避免空转。
    /// </summary>
    public static void Start(string lockKey, string lockHolderId, TimeSpan expire, Func<Task<bool>> tryRenew)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(lockHolderId);
        ArgumentNullException.ThrowIfNull(tryRenew);
        if (expire <= TimeSpan.Zero)
        {
            return;
        }

        var entry = new Entry(lockHolderId, new CancellationTokenSource());
        var cts = entry.Cts;

        while (true)
        {
            if (!Tasks.TryGetValue(lockKey, out var current))
            {
                if (Tasks.TryAdd(lockKey, entry))
                {
                    break;
                }

                continue;
            }

            if (current.HolderId == lockHolderId)
            {
                cts.Dispose();
                return;
            }

            if (Tasks.TryUpdate(lockKey, entry, current))
            {
                current.Cts.Cancel();
                current.Cts.Dispose();
                break;
            }
        }

        var interval = TimeSpan.FromSeconds(expire.TotalSeconds * RenewalThreshold);
        Task.Run(async () =>
        {
            var token = cts.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(interval, token).ConfigureAwait(false);
                        if (token.IsCancellationRequested)
                        {
                            break;
                        }

                        var renewed = await tryRenew().ConfigureAwait(false);
                        if (!renewed)
                        {
                            break;
                        }
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception)
                    {
                        break;
                    }
                }
            }
            finally
            {
                Tasks.TryRemove(new KeyValuePair<string, Entry>(lockKey, entry));
            }
        }, cts.Token);
    }

    /// <summary>停止并移除指定锁的续期循环。锁不存在时无操作。</summary>
    public static void Stop(string lockKey)
    {
        if (string.IsNullOrWhiteSpace(lockKey))
        {
            return;
        }

        if (Tasks.TryRemove(lockKey, out var entry))
        {
            entry.Cts.Cancel();
            entry.Cts.Dispose();
        }
    }
}
