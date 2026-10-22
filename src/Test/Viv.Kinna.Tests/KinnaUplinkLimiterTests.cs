using Viv.Fakes;
using Viv.Kinna.Connections;
using Viv.Kinna.Options;

namespace Viv.Kinna.Tests
{
    public class KinnaUplinkLimiterTests
    {
        private static KinnaUplinkLimiter<TestHub> Create(int permitLimit, int windowSeconds = 10)
        {
            var session = new StubKinnaSession<TestHub>(
                options: new KinnaOptions
                {
                    UplinkPermitLimit = permitLimit,
                    UplinkWindowSeconds = windowSeconds
                });

            return new KinnaUplinkLimiter<TestHub>(session);
        }

        [Fact]
        public void 窗口内配额用完就拒绝()
        {
            using var limiter = Create(permitLimit: 2);

            Assert.True(limiter.TryAcquire("c1"));
            Assert.True(limiter.TryAcquire("c1"));
            Assert.False(limiter.TryAcquire("c1"));
        }

        [Fact]
        public void 配额按连接各算各的()
        {
            using var limiter = Create(permitLimit: 1);

            Assert.True(limiter.TryAcquire("c1"));
            Assert.False(limiter.TryAcquire("c1"));

            // 另一条连接不受影响
            Assert.True(limiter.TryAcquire("c2"));
        }

        [Fact]
        public void 配额配零表示不限流()
        {
            using var limiter = Create(permitLimit: 0);

            for (var i = 0; i < 1000; i++)
            {
                Assert.True(limiter.TryAcquire("c1"));
            }
        }
    }
}
