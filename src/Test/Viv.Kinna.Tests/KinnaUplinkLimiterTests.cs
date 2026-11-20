using Viv.Fakes;
using Viv.Kinna.Connections;
using Viv.Kinna.Options;

namespace Viv.Kinna.Tests
{
    public class KinnaUplinkLimiterTests
    {
        private const KinnaUplinkChannel Command = KinnaUplinkChannel.Command;
        private const KinnaUplinkChannel Heartbeat = KinnaUplinkChannel.Heartbeat;

        private static KinnaUplinkLimiter<TestHub> Create(
            int permitLimit,
            int windowSeconds = 10,
            int heartbeatLimit = 60,
            int heartbeatWindowSeconds = 10)
        {
            var session = new StubKinnaSession<TestHub>(
                options: new KinnaOptions
                {
                    UplinkPermitLimit = permitLimit,
                    UplinkWindowSeconds = windowSeconds,
                    UplinkHeartbeatPermitLimit = heartbeatLimit,
                    UplinkHeartbeatWindowSeconds = heartbeatWindowSeconds
                });

            return new KinnaUplinkLimiter<TestHub>(session);
        }

        [Fact]
        public void 窗口内配额用完就拒绝()
        {
            using var limiter = Create(permitLimit: 2);

            Assert.True(limiter.TryAcquire("c1", Command, out _));
            Assert.True(limiter.TryAcquire("c1", Command, out _));
            Assert.False(limiter.TryAcquire("c1", Command, out _));
        }

        [Fact]
        public void 配额按连接各算各的()
        {
            using var limiter = Create(permitLimit: 1);

            Assert.True(limiter.TryAcquire("c1", Command, out _));
            Assert.False(limiter.TryAcquire("c1", Command, out _));

            // 另一条连接不受影响
            Assert.True(limiter.TryAcquire("c2", Command, out _));
        }

        [Fact]
        public void 心跳和命令各自计数_互不挤占()
        {
            // 大群刷屏把 Ack 的配额吃光，紧跟着的那次 Ping 不该被一起挡掉：
            // 心跳一挡，租约不续，90 秒后连接被清扫 —— 越急越掉线
            using var limiter = Create(permitLimit: 1, heartbeatLimit: 1);

            Assert.True(limiter.TryAcquire("c1", Command, out _));
            Assert.False(limiter.TryAcquire("c1", Command, out _));

            Assert.True(limiter.TryAcquire("c1", Heartbeat, out _));
            Assert.False(limiter.TryAcquire("c1", Heartbeat, out _));
        }

        [Fact]
        public void 心跳配额比命令宽()
        {
            // 同一个连接：命令 20/窗口、心跳 60/窗口
            using var limiter = Create(permitLimit: 20, heartbeatLimit: 60);

            for (var i = 0; i < 20; i++)
            {
                Assert.True(limiter.TryAcquire("c1", Command, out _));
            }

            Assert.False(limiter.TryAcquire("c1", Command, out _));

            // 命令用光了，心跳照常
            for (var i = 0; i < 60; i++)
            {
                Assert.True(limiter.TryAcquire("c1", Heartbeat, out _));
            }

            Assert.False(limiter.TryAcquire("c1", Heartbeat, out _));
        }

        [Fact]
        public void 配额配零表示不限流()
        {
            using var limiter = Create(permitLimit: 0, heartbeatLimit: 0);

            for (var i = 0; i < 1000; i++)
            {
                Assert.True(limiter.TryAcquire("c1", Command, out _));
                Assert.True(limiter.TryAcquire("c1", Heartbeat, out _));
            }

            // 不限流就不该建窗口：建了也只是白占内存
            Assert.Equal(0, limiter.TrackedConnections);
        }

        [Fact]
        public void Release_摘掉窗口并重新开始计数()
        {
            // 框架的 PartitionedRateLimiter 没有淘汰 API，连接 Id 每次重连都是新的，
            // 不释放的话每条历史连接都永久占一格
            using var limiter = Create(permitLimit: 1);

            Assert.True(limiter.TryAcquire("c1", Command, out _));
            Assert.False(limiter.TryAcquire("c1", Command, out _));
            Assert.Equal(1, limiter.TrackedConnections);

            limiter.Release("c1");

            Assert.Equal(0, limiter.TrackedConnections);
            Assert.True(limiter.TryAcquire("c1", Command, out _));
        }

        [Fact]
        public void Release_两个通道的窗口都摘掉()
        {
            using var limiter = Create(permitLimit: 1, heartbeatLimit: 1);

            Assert.True(limiter.TryAcquire("c1", Command, out _));
            Assert.True(limiter.TryAcquire("c1", Heartbeat, out _));
            Assert.Equal(2, limiter.TrackedConnections);

            limiter.Release("c1");

            Assert.Equal(0, limiter.TrackedConnections);
        }

        [Fact]
        public void 通过时退避建议为零()
        {
            using var limiter = Create(permitLimit: 0);

            Assert.True(limiter.TryAcquire("c1", Command, out var retryAfterMs));
            Assert.Equal(0, retryAfterMs);
        }

        [Fact]
        public void 被挡下时给出窗口剩余时间()
        {
            // 只回一个 Throttled 不给时长，客户端只能猜；猜短了就是收到就立刻重报的忙循环
            using var limiter = Create(permitLimit: 1, windowSeconds: 10);

            Assert.True(limiter.TryAcquire("c1", Command, out _));

            Assert.False(limiter.TryAcquire("c1", Command, out var retryAfterMs));

            // 刚用掉配额，剩余应当接近整个窗口，但绝不能是 0 或负数
            Assert.InRange(retryAfterMs, 1, 10_000);
        }

        [Fact]
        public void 退避建议有上限()
        {
            // 窗口配成一天也不该让客户端干等 —— 那种情况它该重连换窗口，而不是坐在那儿等
            using var limiter = Create(permitLimit: 1, windowSeconds: 86_400);

            Assert.True(limiter.TryAcquire("c1", Command, out _));

            Assert.False(limiter.TryAcquire("c1", Command, out var retryAfterMs));
            Assert.Equal(60_000, retryAfterMs);
        }
    }
}
