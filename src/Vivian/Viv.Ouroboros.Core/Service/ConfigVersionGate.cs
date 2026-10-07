using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Viv.Contracts.Attributes;
using Viv.Contracts.Enums;
using Viv.Contracts.Interface;
using Viv.Log;
using Viv.Ouroboros.Core.IService;
using Viv.Redis;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// 版本戳实现。选版本戳而不是订阅广播：广播要维护长连接、重连与订阅生命周期，
    /// 而版本戳只用一次 SET + 一次节流 GET，漏看一次也会在下一轮自然追上，没有"永久失联"这种状态。
    ///
    /// 戳用不透明的 GUID 而不是自增数或时间戳：多实例并发 bump、机器时钟偏差都不会让它"看起来变旧"，
    /// 判等即可。共享戳丢失（Redis 重启 / 过期 / 被清）只会让各实例多清一次缓存，不影响正确性。
    ///
    /// 单例：节流窗口必须进程内唯一，否则每个 Scoped 实例各算各的，等于没节流。
    /// </summary>
    [VivDependency(Lifetime = DependencyLifetime.Singleton)]
    public sealed class ConfigVersionGate : IConfigVersionGate, IDependency
    {
        /// <summary>共享版本戳的键</summary>
        private const string StampKey = "ouroboros:config:version";

        /// <summary>
        /// 最多每 5 秒查一次共享戳。一次 GET 对 Redis 是噪声级别（单实例上限 12 次/分钟），
        /// 而人手点完 refresh 再等 5 秒完全无感 —— 相比它替换掉的 60 秒 TTL 快了一个数量级。
        /// </summary>
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

        /// <summary>Redis 报错后的退避：否则每 5 秒就有一个请求卡在连接超时上</summary>
        private static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(60);

        /// <summary>共享戳的存活期，比任何实例的检查间隔长得多即可；过期只会多清一次缓存</summary>
        private static readonly TimeSpan StampTtl = TimeSpan.FromDays(7);

        private readonly Lazy<IRedisService?> _redis;
        private readonly ILoggerContract _logger;
        private readonly object _lock = new();
        private long _generation;
        private string? _seenStamp;
        private DateTime _nextCheckAt = DateTime.MinValue;
        private int _notEnabledWarned;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="provider">用来**可选**解析 IRedisService：Redis 关闭时它根本没注册，构造注入会直接失败</param>
        /// <param name="logger">日志</param>
        public ConfigVersionGate(IServiceProvider provider, ILoggerContract logger)
        {
            _logger = logger;
            // Lazy + 单例：解析一次，且不会因为首次解析失败就把 null 固化成一辈子的结论
            _redis = new Lazy<IRedisService?>(() => provider.GetService(typeof(IRedisService)) as IRedisService);
        }

        /// <inheritdoc />
        /// <remarks>无锁读：取 Agent 是热路径，而这个值只在锁内自增</remarks>
        public long Generation => Interlocked.Read(ref _generation);

        /// <inheritdoc />
        public long EnsureFresh()
        {
            var redis = _redis.Value;

            // 当前部署（CacheProviderType=0）的正常形态：没有共享戳可看，只清本进程
            if (redis is null) return WarnNotEnabled();

            lock (_lock)
            {
                if (DateTime.UtcNow < _nextCheckAt) return _generation;
                _nextCheckAt = DateTime.UtcNow + CheckInterval;
            }

            string? stamp;
            try
            {
                stamp = redis.Get<string>(StampKey);
            }
            catch (Exception ex)
            {
                // Redis 只是配置传播的加速器：它挂了就退回只清本进程，绝不让"取 Agent"这件事失败
                _logger.Warning("读取配置版本戳失败，本进程退回只清本地缓存：{0}", ex.Message);
                lock (_lock)
                {
                    _nextCheckAt = DateTime.UtcNow + FailureBackoff;
                    return _generation;
                }
            }

            lock (_lock)
            {
                if (string.Equals(stamp, _seenStamp, StringComparison.Ordinal)) return _generation;

                // 戳不同 = 别处改过配置（或戳刚被重建）→ 自增版本，让各缓存的记账对不上从而全清
                _seenStamp = stamp;
                return ++_generation;
            }
        }

        /// <inheritdoc />
        public long Publish()
        {
            var stamp = Guid.NewGuid().ToString("N");
            var redis = _redis.Value;

            if (redis is null)
            {
                WarnNotEnabled();
                return BumpLocal();
            }

            try
            {
                redis.Add(StampKey, stamp, StampTtl);
            }
            catch (Exception ex)
            {
                _logger.Warning("写入配置版本戳失败，本次 refresh 只清本进程缓存：{0}", ex.Message);
                return BumpLocal();
            }

            lock (_lock)
            {
                _seenStamp = stamp;
                return ++_generation;
            }
        }

        /// <summary>自增本进程版本：本实例立刻生效，其余实例只能等 Redis 恢复</summary>
        private long BumpLocal()
        {
            lock (_lock)
            {
                _nextCheckAt = DateTime.UtcNow + CheckInterval;
                return ++_generation;
            }
        }

        /// <summary>没有 Redis 是合法部署形态，只提醒一次，不刷日志</summary>
        private long WarnNotEnabled()
        {
            if (Interlocked.Exchange(ref _notEnabledWarned, 1) == 0)
                _logger.Warning("未启用 Redis，配置变更只在本进程立即生效，其余实例仍等 60 秒 TTL（键 {0}）", StampKey);

            return Generation;
        }
    }
}
