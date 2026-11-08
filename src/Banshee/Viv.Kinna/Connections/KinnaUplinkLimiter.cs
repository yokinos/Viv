using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;

namespace Viv.Kinna.Connections
{
    /// <summary>
    /// 每连接的上行配额，挡的是 Ping、Ack 这类高频小请求。
    ///
    /// 用固定窗口而不是令牌桶：实现简单、上限直观，被挡下的请求客户端下一个窗口照常上报，
    /// 不会因为一次抖动被连续拒绝。配额按连接算，一个进程装多个 Hub 时各有一套。
    /// 配 0 表示不限流，直接放行。
    ///
    /// 不用框架的 <c>PartitionedRateLimiter</c>：它按 key 建的分区没有淘汰 API，
    /// 而连接 Id 每次重连都是新的，等于每条历史连接永久占一个分区（外加一个计时器）。
    /// 这里改成自管的窗口表，连接注销时由协调器 <see cref="Release"/> 摘掉。
    ///
    /// 窗口用 <see cref="Stopwatch"/> 的时间戳算：比较是否越过窗口边界不需要墙钟，
    /// 也就不会因为 NTP 回拨导致窗口永不翻页。
    /// </summary>
    public sealed class KinnaUplinkLimiter<THub> : IDisposable where THub : Hub
    {
        private sealed class Window
        {
            internal long StartTimestamp;
            internal int Count;
        }

        private readonly ConcurrentDictionary<string, Window> _windows = new(StringComparer.Ordinal);
        private readonly int _permitLimit;
        private readonly long _windowTicks;

        public KinnaUplinkLimiter(IKinnaSession<THub> session)
        {
            var options = session.Options;

            // 配 0（或负数）表示不限流，一个窗口都不用建
            if (options.UplinkPermitLimit <= 0)
            {
                _permitLimit = 0;
                return;
            }

            _permitLimit = options.UplinkPermitLimit;
            _windowTicks = (long)(Stopwatch.Frequency * (double)Math.Max(1, options.UplinkWindowSeconds));
        }

        /// <summary>
        /// 不限流时恒 true。
        /// </summary>
        public bool TryAcquire(string connectionId)
        {
            if (_permitLimit == 0) return true;

            var now = Stopwatch.GetTimestamp();
            var window = _windows.GetOrAdd(connectionId, _ => new Window { StartTimestamp = now });

            // 每个连接一个窗口对象，锁的竞争面只有同一条连接上的并发上行（正常是串行的）
            lock (window)
            {
                if (now - window.StartTimestamp >= _windowTicks)
                {
                    window.StartTimestamp = now;
                    window.Count = 1;
                    return true;
                }

                if (window.Count >= _permitLimit) return false;

                window.Count++;
                return true;
            }
        }

        /// <summary>
        /// 连接注销时摘掉它的窗口。不摘的话每条历史连接都留一个：窗口不会自己过期。
        /// </summary>
        public void Release(string connectionId) => _windows.TryRemove(connectionId, out _);

        /// <summary>当前在跟踪的连接数，排障和测试用。</summary>
        public int TrackedConnections => _windows.Count;

        public void Dispose() => _windows.Clear();
    }
}
