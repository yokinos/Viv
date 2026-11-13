using Microsoft.Extensions.DependencyInjection;
using Viv.Redis;

namespace Viv.Engine.RateLimit
{
    /// <summary>
    /// 把计数放在 Redis 里，所有网关副本共用一个数。
    ///
    /// 之前是每个进程各持一个 <c>FixedWindowRateLimiter</c>：单副本时正确，扩到 N 个副本后
    /// 同一个 IP 每台各给一份配额，配置写「每 10 秒 100 次」实际放进来 N×100 次 ——
    /// 限流不会报错、不会告警，只是默默失效。
    /// </summary>
    internal sealed class RedisRateLimitCounter : IRateLimitCounter
    {
        /// <summary>
        /// INCR 和 EXPIRE 必须一起做：分两次调用的话，进程在两条命令之间崩掉会留下永不过期的键，
        /// 那个客户端就被永久计数、永远 429。脚本在 Redis 里是单线程执行的，两条命令之间不会被打断。
        /// </summary>
        private const string IncrementScript =
            """
            local current = redis.call('INCR', KEYS[1])
            if current == 1 then
                redis.call('PEXPIRE', KEYS[1], ARGV[1])
            end
            return current
            """;

        private readonly IServiceProvider _provider;

        public RedisRateLimitCounter(IServiceProvider provider)
        {
            _provider = provider;
        }

        public async Task<long?> TryIncrementAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            // Redis 是可选件：没选它做缓存提供者时根本没有 IRedisService，这时交给调用方走本机兜底
            if (_provider.GetService<IRedisService>() is not { } redis)
            {
                return null;
            }

            try
            {
                var result = await redis.ExecuteScriptAsync(
                    IncrementScript,
                    key,
                    (long)Math.Max(1, ttl.TotalMilliseconds)).ConfigureAwait(false);

                return result.IsNull ? null : (long)result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // 连不上、超时、集群槽位不可用 —— 统一当「计数器不可用」，
                // 由中间件降级成本机限流。在这里吞掉是有意的：限流器不该把请求变成 500。
                return null;
            }
        }
    }
}
