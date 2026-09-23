using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Viv.Redis
{
    /// <summary>
    /// Redis 访问的运行指标。Meter 名 Viv.Redis，Aspire ServiceDefaults 会把它挂进 OTel。
    ///
    /// 与 NanaMetrics / OutboxMetrics 不同的是，这里的 Record* 是 public：埋点分散在别的程序集
    /// （分布式锁在 Viv.Engine、缓存回源在 Viv.Momo），internal 就够不着。Meter 与仪表本身仍是 internal。
    /// </summary>
    public static class RedisMetrics
    {
        public const string MeterName = "Viv.Redis";

        public const string CommandDurationInstrument = "viv.redis.command.duration";
        public const string ErrorsInstrument = "viv.redis.errors";
        public const string ConnectionFailedInstrument = "viv.redis.connection.failed";
        public const string ConnectionRestoredInstrument = "viv.redis.connection.restored";
        public const string CacheResultInstrument = "viv.redis.cache.result";
        public const string FallbackInstrument = "viv.redis.fallback";
        public const string LockAcquireDurationInstrument = "viv.redis.lock.acquire.duration";
        public const string LockRenewalStoppedInstrument = "viv.redis.lock.renewal.stopped";
        public const string LockReleaseFailedInstrument = "viv.redis.lock.release.failed";

        internal static readonly Meter Meter = new(MeterName, "1.0.0");

        internal static readonly Histogram<double> CommandDuration = Meter.CreateHistogram<double>(CommandDurationInstrument, "ms");
        internal static readonly Counter<long> Errors = Meter.CreateCounter<long>(ErrorsInstrument);
        internal static readonly Counter<long> ConnectionFailed = Meter.CreateCounter<long>(ConnectionFailedInstrument);
        internal static readonly Counter<long> ConnectionRestored = Meter.CreateCounter<long>(ConnectionRestoredInstrument);
        internal static readonly Counter<long> CacheResult = Meter.CreateCounter<long>(CacheResultInstrument);
        internal static readonly Counter<long> Fallback = Meter.CreateCounter<long>(FallbackInstrument);
        internal static readonly Histogram<double> LockAcquireDuration = Meter.CreateHistogram<double>(LockAcquireDurationInstrument, "ms");
        internal static readonly Counter<long> LockRenewalStopped = Meter.CreateCounter<long>(LockRenewalStoppedInstrument);
        internal static readonly Counter<long> LockReleaseFailed = Meter.CreateCounter<long>(LockReleaseFailedInstrument);

        /// <summary>
        /// 一次 Redis 命令的耗时，成败都记。失败时同时累加 Errors。
        /// 调用方是四个 ExecuteRedis* 重载，所以这里拿不到命令名，只有耗时与成败。
        /// </summary>
        public static void RecordCommand(long elapsedMs, bool success)
        {
            var tags = new TagList { { "result", success ? "ok" : "error" } };
            CommandDuration.Record(elapsedMs, tags);

            if (!success)
            {
                Errors.Add(1);
            }
        }

        /// <summary>
        /// 连接断开。由 StackExchange.Redis 的 ConnectionFailed 事件驱动，不是某次调用失败。
        /// </summary>
        public static void RecordConnectionFailed(string endpoint)
        {
            var tags = new TagList { { "endpoint", endpoint } };
            ConnectionFailed.Add(1, tags);
        }

        /// <summary>
        /// 连接恢复。与 RecordConnectionFailed 配对，两者速率一起看才是「抖了多久」。
        /// </summary>
        public static void RecordConnectionRestored(string endpoint)
        {
            var tags = new TagList { { "endpoint", endpoint } };
            ConnectionRestored.Add(1, tags);
        }

        /// <summary>
        /// 缓存读的结果，用来算命中率。
        /// </summary>
        public static void RecordCache(bool hit)
        {
            var tags = new TagList { { "result", hit ? "hit" : "miss" } };
            CacheResult.Add(1, tags);
        }

        /// <summary>
        /// 回源数据库。reason 取 cache（缓存或锁不可用）或 lock（取锁不可用）。
        /// 这是「Redis 挂了，库压力翻倍」在面板上的唯一信号。
        /// </summary>
        public static void RecordFallback(string reason)
        {
            var tags = new TagList { { "reason", reason } };
            Fallback.Add(1, tags);
        }

        /// <summary>
        /// 取锁的整体耗时（含重试等待）。acquired 为 false 表示重试耗尽仍未拿到。
        /// </summary>
        public static void RecordLockAcquire(long elapsedMs, bool acquired)
        {
            var tags = new TagList { { "result", acquired ? "acquired" : "timeout" } };
            LockAcquireDuration.Record(elapsedMs, tags);
        }

        /// <summary>
        /// 锁续期循环非正常停转。停转之后这把锁再没人续期，业务跑到一半就会掉锁，所以单独计数。
        /// </summary>
        public static void RecordLockRenewalStopped() => LockRenewalStopped.Add(1);

        /// <summary>
        /// 释放锁失败。锁会等到自然过期，这期间别的实例进不来。
        /// </summary>
        public static void RecordLockReleaseFailed() => LockReleaseFailed.Add(1);
    }
}
