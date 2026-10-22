using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Viv.Kinna.Connections;

namespace Viv.Kinna.Health
{
    /// <summary>
    /// 连接数健康检查。回答两个问题：现在多少连接、清扫还在不在跑。
    ///
    /// 清扫卡死时进程是「健康的」但连接会一直涨到租约永远不生效，所以单看进程存活不够。
    /// 清扫超过三个周期没跑记 Degraded；最久没续租的连接超过两倍租约也记 Degraded
    /// （说明清扫没跟上，或者客户端心跳间隔与租约配置不匹配）。
    /// </summary>
    public sealed class KinnaConnectionHealthCheck<THub> : IHealthCheck where THub : Hub
    {
        private readonly IKinnaSession<THub> _session;
        private readonly KinnaSweepClock<THub> _clock;

        public KinnaConnectionHealthCheck(IKinnaSession<THub> session, KinnaSweepClock<THub> clock)
        {
            _session = session;
            _clock = clock;
        }

        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            var options = _session.Options;
            var snapshot = _session.Pool.GetSnapshot();

            var data = new Dictionary<string, object>
            {
                ["hub"] = _session.Hub,
                ["connections"] = snapshot.Connections,
                ["maxIdleSeconds"] = (int)snapshot.MaxIdle.TotalSeconds
            };

            var interval = TimeSpan.FromSeconds(Math.Max(1, options.SweepIntervalSeconds));
            var ttl = TimeSpan.FromSeconds(Math.Max(1, options.EntryTtlSeconds));

            if (_clock.LastSweepAt is not { } lastSweep)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"[{_session.Hub}] 清扫还没跑过", data: data));
            }

            var sinceSweep = DateTimeOffset.UtcNow - lastSweep;
            data["secondsSinceSweep"] = (int)sinceSweep.TotalSeconds;
            data["lastSweepRemoved"] = _clock.LastRemoved;

            if (sinceSweep > interval * 3)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"[{_session.Hub}] 清扫已 {(int)sinceSweep.TotalSeconds}s 没跑（间隔 {(int)interval.TotalSeconds}s）",
                    data: data));
            }

            if (snapshot.MaxIdle > ttl * 2)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"[{_session.Hub}] 最久未续租的连接 {(int)snapshot.MaxIdle.TotalSeconds}s，超过两倍租约",
                    data: data));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                $"[{_session.Hub}] {snapshot.Connections} 个连接", data));
        }
    }
}
