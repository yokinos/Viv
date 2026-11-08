using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Time.Testing;
using Viv.Fakes;
using Viv.Kinna.Connections;
using Viv.Kinna.Health;
using Viv.Kinna.Options;

namespace Viv.Kinna.Tests
{
    public class KinnaConnectionHealthCheckTests
    {
        private static (KinnaConnectionHealthCheck<TestHub> Check, KinnaSweepClock<TestHub> Clock, FakeTimeProvider Time) Create(
            int connections = 2,
            TimeSpan? maxIdle = null,
            int sweepIntervalSeconds = 60,
            int entryTtlSeconds = 90)
        {
            var pool = new StubKinnaConnectionPool
            {
                Snapshot = new KinnaConnectionPoolSnapshot(connections, maxIdle ?? TimeSpan.FromSeconds(5))
            };

            var session = new StubKinnaSession<TestHub>(
                pool,
                options: new KinnaOptions
                {
                    SweepIntervalSeconds = sweepIntervalSeconds,
                    EntryTtlSeconds = entryTtlSeconds
                });

            // 清扫器和健康检查共用同一个时钟：差几秒这种判断只有时间可控时才测得出边界
            var time = new FakeTimeProvider();
            var clock = new KinnaSweepClock<TestHub>(time);
            return (new KinnaConnectionHealthCheck<TestHub>(session, clock, time), clock, time);
        }

        [Fact]
        public async Task 清扫正常_报告连接数()
        {
            var (check, clock, _) = Create(connections: 7);
            clock.Record(0);

            var result = await check.CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Healthy, result.Status);
            Assert.Equal(7, result.Data["connections"]);
        }

        [Fact]
        public async Task 清扫还没跑过_降级()
        {
            // 刚启动、或者后台循环起不来，这时候进程健康但租约不受控
            var (check, _, _) = Create();

            var result = await check.CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Degraded, result.Status);
            Assert.Contains("清扫还没跑过", result.Description);
        }

        [Fact]
        public async Task 清扫卡住超过三个周期_降级()
        {
            var (check, clock, time) = Create(sweepIntervalSeconds: 10);
            clock.Record(0);

            // 上次清扫停在 T0，时钟往前走 31 秒：三个周期是 30 秒，刚好越界
            time.Advance(TimeSpan.FromSeconds(31));

            var result = await check.CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Degraded, result.Status);
            Assert.Contains("没跑", result.Description);
        }

        [Fact]
        public async Task 清扫刚好三个周期内_不降级()
        {
            // 边界：判据是 sinceSweep > interval * 3，等于三个周期还算正常
            var (check, clock, time) = Create(sweepIntervalSeconds: 10);
            clock.Record(0);
            time.Advance(TimeSpan.FromSeconds(30));

            var result = await check.CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Healthy, result.Status);
        }

        [Fact]
        public async Task 最久未续租超过两倍租约_降级()
        {
            // 客户端心跳间隔和租约配置对不上，或者清扫没跟上
            var (check, clock, _) = Create(maxIdle: TimeSpan.FromSeconds(200), entryTtlSeconds: 90);
            clock.Record(0);

            var result = await check.CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Degraded, result.Status);
            Assert.Contains("超过两倍租约", result.Description);
        }

        [Fact]
        public async Task 没有连接时也算健康()
        {
            var (check, clock, _) = Create(connections: 0, maxIdle: TimeSpan.Zero);
            clock.Record(0);

            var result = await check.CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Healthy, result.Status);
            Assert.Equal(0, result.Data["connections"]);
        }
    }
}
