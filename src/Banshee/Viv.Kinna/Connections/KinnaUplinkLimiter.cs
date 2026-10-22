using System.Threading.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Viv.Kinna.Connections;
using Viv.Kinna.Options;

namespace Viv.Kinna.Connections
{
    /// <summary>
    /// 每连接的上行配额，挡的是 Ping、Ack 这类高频小请求。
    ///
    /// 用固定窗口而不是令牌桶：实现简单、上限直观，被挡下的请求客户端下一个窗口照常上报，
    /// 不会因为一次抖动被连续拒绝。配额按连接算，一个进程装多个 Hub 时各有一套。
    /// 配 0 表示不限流，直接放行。
    /// </summary>
    public sealed class KinnaUplinkLimiter<THub> : IDisposable where THub : Hub
    {
        private readonly PartitionedRateLimiter<string>? _limiter;

        public KinnaUplinkLimiter(IKinnaSession<THub> session)
        {
            var options = session.Options;
            if (options.UplinkPermitLimit <= 0)
                return;

            var window = TimeSpan.FromSeconds(Math.Max(1, options.UplinkWindowSeconds));

            _limiter = PartitionedRateLimiter.Create<string, string>(connectionId =>
                RateLimitPartition.GetFixedWindowLimiter(connectionId, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.UplinkPermitLimit,
                    Window = window,
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
        }

        /// <summary>
        /// 不限流时恒 true。
        /// </summary>
        public bool TryAcquire(string connectionId)
        {
            if (_limiter == null) return true;

            using var lease = _limiter.AttemptAcquire(connectionId);
            return lease.IsAcquired;
        }

        public void Dispose() => _limiter?.Dispose();
    }
}
