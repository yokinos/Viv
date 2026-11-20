using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;

namespace Viv.Kinna.Connections
{
    /// <summary>
    /// 上行通道。两档的成本差一个数量级，配额必须分开算。
    /// </summary>
    public enum KinnaUplinkChannel
    {
        /// <summary>
        /// 心跳续租。每次 2-3 次 Redis 往返（<c>EXIST</c> + <c>EXPIRE</c>，键过期时多一次
        /// <c>INCREMENT</c>），谈不上便宜，所以需要挡；但它也是「不续租就被清扫」的保命操作，
        /// 被挡一次就可能掉线。配额给得比 <see cref="Command"/> 宽得多。
        /// </summary>
        Heartbeat = 0,

        /// <summary>
        /// 确认与对齐（Ack / Resync）。每次一次存储往返，量大的时候才是真正的成本。
        /// </summary>
        Command = 1
    }

    /// <summary>
    /// 每连接的上行配额，挡的是 Ping、Ack 这类高频小请求。
    ///
    /// 用固定窗口而不是令牌桶：实现简单、上限直观，被挡下的请求客户端下一个窗口照常上报，
    /// 不会因为一次抖动被连续拒绝。配额按连接算，一个进程装多个 Hub 时各有一套。
    /// 配 0 表示该通道不限流，直接放行。
    ///
    /// **心跳和命令分两档**：共用一个桶时，大群刷屏把 Ack 的配额吃光，紧跟着的那次 Ping
    /// 就会被挡掉 —— 而 Ping 一挡，租约不续，90 秒后连接被清扫，客户端掉线重连，
    /// 重连又要一次性补拉。越急越掉线。两档各自独立计数就没这个耦合。
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
        /// <summary>
        /// <c>RetryAfterMs</c> 的上限。窗口配得再长也不让客户端等超过一分钟——
        /// 那种情况下重连（换窗口、换连接 Id）比干等更快。
        /// </summary>
        private const int MaxRetryAfterMs = 60_000;

        private sealed class Window
        {
            internal long StartTimestamp;
            internal int Count;
        }

        /// <summary>一个通道的窗口表与配额。两个通道各一份，互不影响。</summary>
        private sealed class Bucket
        {
            internal readonly ConcurrentDictionary<string, Window> Windows = new(StringComparer.Ordinal);
            internal readonly int PermitLimit;
            internal readonly long WindowTicks;

            internal Bucket(int permitLimit, long windowTicks)
            {
                PermitLimit = permitLimit;
                WindowTicks = windowTicks;
            }
        }

        private readonly Bucket _heartbeat;
        private readonly Bucket _command;

        public KinnaUplinkLimiter(IKinnaSession<THub> session)
        {
            var options = session.Options;

            _heartbeat = new Bucket(
                options.UplinkHeartbeatPermitLimit,
                TicksOf(options.UplinkHeartbeatWindowSeconds));

            _command = new Bucket(
                options.UplinkPermitLimit,
                TicksOf(options.UplinkWindowSeconds));
        }

        /// <summary>
        /// 取一个上行配额。对应通道不限流（配额配 0）时恒 true。
        /// </summary>
        /// <param name="retryAfterMs">
        /// 被挡下时是「窗口还剩多久」，即客户端最早能通过的时刻；通过时为 0。
        ///
        /// 只回一个 <c>Throttled</c> 而不给时长，客户端只能猜：猜短了就是「收到就立刻重报」的
        /// 忙循环，猜长了白等一个窗口。有了这个值它可以按服务端的节奏退避。
        ///
        /// 它是**下限**不是精确值：客户端仍应在这个基础上做自己的指数退避，
        /// 否则窗口边界上所有被挡的连接会同一毫秒一起回来，把下一个窗口瞬间打满。
        /// </param>
        public bool TryAcquire(string connectionId, KinnaUplinkChannel channel, out int retryAfterMs)
        {
            retryAfterMs = 0;

            var bucket = Resolve(channel);
            if (bucket.PermitLimit <= 0) return true;

            var now = Stopwatch.GetTimestamp();
            var window = bucket.Windows.GetOrAdd(connectionId, _ => new Window { StartTimestamp = now });

            // 每个连接一个窗口对象，锁的竞争面只有同一条连接上的并发上行（正常是串行的）
            lock (window)
            {
                var elapsed = now - window.StartTimestamp;

                if (elapsed >= bucket.WindowTicks)
                {
                    window.StartTimestamp = now;
                    window.Count = 1;
                    return true;
                }

                if (window.Count >= bucket.PermitLimit)
                {
                    retryAfterMs = RemainingMs(bucket.WindowTicks - elapsed);
                    return false;
                }

                window.Count++;
                return true;
            }
        }

        /// <summary>
        /// 剩下的等多久才允许通过。
        ///
        /// 下界给 1ms 而不是 0：回 0 等于告诉客户端「立刻重试」，而窗口还没翻页，
        /// 结果是原地忙循环——CPU、日志、限流指标一起涨，问题反而更明显。
        ///
        /// 上界给 60s：窗口配得再长也没有意义。超过一分钟的等待，客户端该重连而不是干等，
        /// 而重连会拿到新的连接 Id、新的窗口，比坐在那儿等更划算。
        /// </summary>
        private static int RemainingMs(long remainingTicks)
        {
            var ms = (long)Math.Ceiling(remainingTicks * 1000.0 / Stopwatch.Frequency);
            return (int)Math.Clamp(ms, 1, MaxRetryAfterMs);
        }

        private static long TicksOf(int windowSeconds)
            => (long)(Stopwatch.Frequency * (double)Math.Max(1, windowSeconds));

        private Bucket Resolve(KinnaUplinkChannel channel)
            => channel == KinnaUplinkChannel.Heartbeat ? _heartbeat : _command;

        /// <summary>
        /// 连接注销时摘掉它在两个通道里的窗口。不摘的话每条历史连接都留两个：
        /// 窗口不会自己过期。
        /// </summary>
        public void Release(string connectionId)
        {
            _heartbeat.Windows.TryRemove(connectionId, out _);
            _command.Windows.TryRemove(connectionId, out _);
        }

        /// <summary>当前在跟踪的连接数（两个通道合计），排障和测试用。</summary>
        public int TrackedConnections => _heartbeat.Windows.Count + _command.Windows.Count;

        public void Dispose()
        {
            _heartbeat.Windows.Clear();
            _command.Windows.Clear();
        }
    }
}
