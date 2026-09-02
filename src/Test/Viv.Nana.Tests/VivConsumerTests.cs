using Viv.Contracts.Interface;
using Viv.Delusion;
using Viv.Nana.Core;
using Viv.Nana.Options;

namespace Viv.Nana.Tests
{
    /// <summary>成功消费</summary>
    public class SuccessConsumer : VivConsumer<TestApexEvent>
    {
        public SuccessConsumer(VivConsumerDependency dependency) : base(dependency) { }
        public override Task<SubscribeResult> ReceiveMessageAsync(NanaEnvelope<TestApexEvent> envelope, CancellationToken cancellationToken = default)
            => Task.FromResult(SubscribeResult.Success());
    }

    /// <summary>失败并要求重投（触发 VivRequeueException → Wolverine 重试）</summary>
    public class RequeueConsumer : VivConsumer<TestApexEvent>
    {
        public RequeueConsumer(VivConsumerDependency dependency) : base(dependency) { }
        public override Task<SubscribeResult> ReceiveMessageAsync(NanaEnvelope<TestApexEvent> envelope, CancellationToken cancellationToken = default)
            => Task.FromResult(SubscribeResult.Failed(true, "业务处理失败，重投"));
    }

    /// <summary>失败但不重投（记日志丢弃）</summary>
    public class DropConsumer : VivConsumer<TestApexEvent>
    {
        public DropConsumer(VivConsumerDependency dependency) : base(dependency) { }
        public override Task<SubscribeResult> ReceiveMessageAsync(NanaEnvelope<TestApexEvent> envelope, CancellationToken cancellationToken = default)
            => Task.FromResult(SubscribeResult.Failed(false, "不可重试，丢弃"));
    }

    /// <summary>失败后调用 RedeliverAsync 延迟重投</summary>
    public class RedeliverConsumer : VivConsumer<TestApexEvent>
    {
        public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(5);

        public RedeliverConsumer(VivConsumerDependency dependency) : base(dependency) { }

        public override Task<SubscribeResult> ReceiveMessageAsync(NanaEnvelope<TestApexEvent> envelope, CancellationToken cancellationToken = default)
            => RedeliverAsync(envelope, Delay, cancellationToken);
    }

    /// <summary>记录业务是否进入</summary>
    public class CountingConsumer : VivConsumer<TestApexEvent>
    {
        public int Calls { get; private set; }

        public CountingConsumer(VivConsumerDependency dependency) : base(dependency) { }

        public override Task<SubscribeResult> ReceiveMessageAsync(NanaEnvelope<TestApexEvent> envelope, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(SubscribeResult.Success());
        }
    }

    /// <summary>
    /// 消费者重试/确认/丢弃语义 —— VivConsumer.HandleAsync 是 Wolverine handler 入口，
    /// 结果映射到框架行为（确认 / 抛 VivRequeueException / 记日志丢弃）。
    /// </summary>
    public class VivConsumerTests
    {
        private static NanaEnvelope<TestApexEvent> Envelope(TestApexEvent? content = null)
            => new() { Content = content ?? new TestApexEvent { Payload = "data" } };

        private static VivConsumerDependency Dep(StubLogger logger, StubPublisher publisher, IDistributedLock? distributedLock = null)
            => new(logger, new FakeContext(), publisher, distributedLock);

        [Fact]
        public async Task 成功_无异常无日志()
        {
            var logger = new StubLogger();
            var consumer = new SuccessConsumer(Dep(logger, new StubPublisher()));

            await consumer.HandleAsync(Envelope(), CancellationToken.None);

            Assert.Empty(logger.Errors);
            Assert.Empty(logger.ErrorWithException);
        }

        [Fact]
        public async Task 重投_抛VivRequeueException()
        {
            var consumer = new RequeueConsumer(Dep(new StubLogger(), new StubPublisher()));

            var ex = await Assert.ThrowsAsync<VivRequeueException>(() => consumer.HandleAsync(Envelope(), CancellationToken.None));

            Assert.Contains("重投", ex.Message);
        }

        [Fact]
        public async Task 失败不回队_记日志不抛异常()
        {
            var logger = new StubLogger();
            var consumer = new DropConsumer(Dep(logger, new StubPublisher()));

            await consumer.HandleAsync(Envelope(), CancellationToken.None);

            Assert.Empty(logger.ErrorWithException);
            var error = Assert.Single(logger.Errors);
            Assert.Contains("消息消费失败", error);
            Assert.Contains("丢弃", error);
            Assert.Contains("MessageId", error);
        }

        [Fact]
        public async Task 空消息_直接返回不处理()
        {
            var consumer = new SuccessConsumer(Dep(new StubLogger(), new StubPublisher()));

            await consumer.HandleAsync(null!, CancellationToken.None);

            Assert.True(true); // 到达这里即未抛异常
        }

        [Fact]
        public async Task 内容为空_直接返回不处理()
        {
            var logger = new StubLogger();
            var consumer = new DropConsumer(Dep(logger, new StubPublisher()));

            await consumer.HandleAsync(new NanaEnvelope<TestApexEvent> { Content = null }, CancellationToken.None);

            Assert.Empty(logger.Errors);
        }

        [Fact]
        public async Task 延迟重投_未超上限_投递并计数()
        {
            VivConfigRegistry.Add(new NanaOptions { RetryCount = 3 });
            try
            {
                var logger = new StubLogger();
                var publisher = new StubPublisher();
                var consumer = new RedeliverConsumer(Dep(logger, publisher));
                var envelope = Envelope();

                await consumer.HandleAsync(envelope, CancellationToken.None);

                Assert.True(publisher.PublishDelayCalled);
                var scheduled = Assert.IsType<NanaEnvelope<TestApexEvent>>(publisher.LastEnvelope);
                Assert.Equal(1, scheduled.ReDeliverCount);          // 原消息 +1，重投副本继承
                Assert.Equal(5, scheduled.DelaySecond);             // DelaySecond 携带延迟值
                Assert.Empty(logger.Errors);
            }
            finally
            {
                VivConfigRegistry.Remove<NanaOptions>();
            }
        }

        [Fact]
        public async Task 延迟重投_超上限_丢弃不投递()
        {
            VivConfigRegistry.Add(new NanaOptions { RetryCount = 3 });
            try
            {
                var logger = new StubLogger();
                var publisher = new StubPublisher();
                var consumer = new RedeliverConsumer(Dep(logger, publisher));
                var envelope = Envelope();
                envelope.ReDeliverCount = 3;                        // 已达上限

                await consumer.HandleAsync(envelope, CancellationToken.None);

                Assert.False(publisher.PublishDelayCalled);
                Assert.Equal(3, envelope.ReDeliverCount);           // 未再自增
                var warning = Assert.Single(logger.Warnings);
                Assert.Contains("上限", warning);
            }
            finally
            {
                VivConfigRegistry.Remove<NanaOptions>();
            }
        }

        [Fact]
        public async Task 取到锁_进入业务并释放()
        {
            var logger = new StubLogger();
            var distributedLock = new StubDistributedLock { AcquireResult = true };
            var consumer = new CountingConsumer(Dep(logger, new StubPublisher(), distributedLock));
            var envelope = Envelope();
            envelope.MessageId = 42;

            await consumer.HandleAsync(envelope, CancellationToken.None);

            Assert.Equal(1, consumer.Calls);
            Assert.Equal(1, distributedLock.AcquireCalls);
            Assert.Equal(1, distributedLock.ReleaseCalls);
            Assert.Equal(NanaRegister.GetConsumerLockKey(nameof(TestApexEvent), 42), distributedLock.LastLockKey);
            Assert.Empty(logger.Warnings);
        }

        [Fact]
        public async Task 拿不到锁_不进业务_记警告()
        {
            var logger = new StubLogger();
            var distributedLock = new StubDistributedLock { AcquireResult = false };
            var consumer = new CountingConsumer(Dep(logger, new StubPublisher(), distributedLock));

            await consumer.HandleAsync(Envelope(), CancellationToken.None);

            Assert.Equal(0, consumer.Calls);
            Assert.Equal(1, distributedLock.AcquireCalls);
            Assert.Equal(0, distributedLock.ReleaseCalls);
            var warning = Assert.Single(logger.Warnings);
            Assert.Contains("获取分布式锁失败", warning);
        }

        [Fact]
        public async Task 拿不到锁且声明重投_延迟重投()
        {
            VivConfigRegistry.Add(new NanaOptions { RetryCount = 3 });
            try
            {
                var logger = new StubLogger();
                var publisher = new StubPublisher();
                var distributedLock = new StubDistributedLock { AcquireResult = false };
                var consumer = new CountingConsumer(Dep(logger, publisher, distributedLock));
                var envelope = Envelope(new TestApexEvent { LockFailShouldRetryDeliver = true });

                await consumer.HandleAsync(envelope, CancellationToken.None);

                Assert.Equal(0, consumer.Calls);
                Assert.True(publisher.PublishDelayCalled);
                Assert.Empty(logger.Warnings.FindAll(w => w.Contains("丢弃")));
            }
            finally
            {
                VivConfigRegistry.Remove<NanaOptions>();
            }
        }
    }
}
