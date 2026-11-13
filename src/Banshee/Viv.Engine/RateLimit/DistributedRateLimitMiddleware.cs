using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Viv.Engine.Options;
using Viv.Log;

namespace Viv.Engine.RateLimit
{
    /// <summary>
    /// 按客户端 IP 的固定窗口限流，计数放在 Redis（<see cref="IRateLimitCounter"/>），
    /// 所以多个网关副本共用一份配额。
    ///
    /// 用它替掉 <c>UseRateLimiter()</c> + <c>AddFixedWindowLimiter</c>：框架那套的分区工厂是同步签名
    /// （<c>Func&lt;TKey, RateLimitLease&gt;</c>），拿不到 Redis 的异步结果，只能在请求线程上阻塞等待，
    /// 在网关这种高并发入口是明摆着的线程池风险。
    ///
    /// 降级是分层的，不是全放或全拦：
    /// <list type="bullet">
    /// <item>有 Redis、Redis 正常 → 全副本共享计数</item>
    /// <item>没配 Redis，或 Redis 这一刻不可用 → 退回本机分区计数（每副本各一份，和扩容前的行为一致）</item>
    /// <item>PermitLimit &lt;= 0 → 完全不限流（配置语义，不是降级）</item>
    /// </list>
    /// 降级时不会放行全部请求：那等于「Redis 一抖，防刷就没了」，比多放几倍请求更糟。
    /// </summary>
    internal sealed class DistributedRateLimitMiddleware
    {
        /// <summary>键前缀。带上窗口时间片，所以不同窗口是不同的键，过期即淘汰</summary>
        private const string KeyPrefix = "viv:rl:";

        /// <summary>降级告警只打一次：Redis 挂掉期间每个请求都打一行会把日志淹掉</summary>
        private static int _degradedWarned;

        private readonly RequestDelegate _next;
        private readonly IOptionsMonitor<VivRateLimitOptions> _options;
        private readonly IRateLimitCounter _counter;
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// 本机兜底限流器。保留原实现的分区方式（IP + 当前配置值），
        /// 配置热重载后按新配额重新分区，不需要重启。
        /// </summary>
        private readonly PartitionedRateLimiter<string> _fallback;

        public DistributedRateLimitMiddleware(
            RequestDelegate next,
            IOptionsMonitor<VivRateLimitOptions> options,
            IRateLimitCounter counter,
            IServiceProvider serviceProvider)
        {
            _next = next;
            _options = options;
            _counter = counter;
            _serviceProvider = serviceProvider;

            _fallback = PartitionedRateLimiter.Create<string, string>(client =>
            {
                var current = _options.CurrentValue;
                var partition = string.Create(CultureInfo.InvariantCulture,
                    $"{client}|{current.PermitLimit}|{current.Window}|{current.QueueLimit}");

                return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = current.PermitLimit,
                    Window = TimeSpan.FromSeconds(Math.Max(1, current.Window)),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = Math.Max(0, current.QueueLimit)
                });
            });
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var options = _options.CurrentValue;

            // 约定：PermitLimit <= 0 表示不限流
            if (options.PermitLimit <= 0)
            {
                await _next(context).ConfigureAwait(false);
                return;
            }

            // UseForwardedHeaders 已把 RemoteIpAddress 换成真实客户端 IP
            var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var window = TimeSpan.FromSeconds(Math.Max(1, options.Window));
            var windowIndex = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / (long)window.TotalSeconds;
            var key = string.Create(CultureInfo.InvariantCulture, $"{KeyPrefix}{client}:{windowIndex}");

            // 键的存活时间取窗口两倍：留一个窗口的余量，避免请求恰好落在边界上时读到刚过期的键
            var used = await _counter.TryIncrementAsync(key, window * 2, context.RequestAborted).ConfigureAwait(false);

            if (used is { } count)
            {
                if (count > options.PermitLimit)
                {
                    await RejectAsync(context, window).ConfigureAwait(false);
                    return;
                }

                await _next(context).ConfigureAwait(false);
                return;
            }

            WarnDegradedOnce();
            await FallbackAsync(context, client, window).ConfigureAwait(false);
        }

        /// <summary>
        /// 计数器不可用时的本机限流。手里还有每个副本各自的配额，
        /// 总比完全不拦好 —— 这批请求最多被放大 N 倍，而不是无限。
        /// </summary>
        private async Task FallbackAsync(HttpContext context, string client, TimeSpan window)
        {
            using var lease = await _fallback.AcquireAsync(client, permitCount: 1, context.RequestAborted)
                .ConfigureAwait(false);

            if (!lease.IsAcquired)
            {
                await RejectAsync(context, window).ConfigureAwait(false);
                return;
            }

            await _next(context).ConfigureAwait(false);
        }

        private static Task RejectAsync(HttpContext context, TimeSpan window)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;

            // 不排队的话客户端最早也要等一个窗口；带 QueueLimit 的兜底路径略保守，给整窗口是安全的
            context.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(window.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

            return Task.CompletedTask;
        }

        private void WarnDegradedOnce()
        {
            if (Interlocked.Exchange(ref _degradedWarned, 1) == 1)
            {
                return;
            }

            if (_serviceProvider.GetService(typeof(ILoggerContract)) is ILoggerContract logger)
            {
                logger.Warning(
                    "限流计数器不可用（未配置 Redis 或 Redis 暂时连不上），已退回本机分区限流：" +
                    "每个网关副本各算一份配额，实际阈值会随副本数放大");
            }
        }
    }
}
