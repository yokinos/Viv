using Microsoft.Extensions.Options;
using Viv.Contracts;
using Viv.Contracts.Enums;
using Viv.Contracts.Exceptions;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Delusion;
using Viv.Fakes;
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

    /// <summary>重写锁 Key 返回 null —— 这条消息不该进锁逻辑</summary>
    public class NoLockConsumer : VivConsumer<TestApexEvent>
    {
        public int Calls { get; private set; }

        public NoLockConsumer(VivConsumerDependency dependency) : base(dependency) { }

        protected override object? GetConsumeLockKey(NanaEnvelope<TestApexEvent> envelope) => null;

        public override Task<SubscribeResult> ReceiveMessageAsync(NanaEnvelope<TestApexEvent> envelope, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(SubscribeResult.Success());
        }
    }

    /// <summary>把锁换业务粒度，并在拼 Key 时读本条消息的租户与锁持有者</summary>
    public class BusinessLockConsumer : VivConsumer<TestApexEvent>
    {
        public long SeenTenantId { get; private set; }

        public string? SeenHolderId { get; private set; }

        public BusinessLockConsumer(VivConsumerDependency dependency) : base(dependency) { }

        protected override object? GetConsumeLockKey(NanaEnvelope<TestApexEvent> envelope)
        {
            SeenTenantId = _context.SubjectId;
            SeenHolderId = LockHolderContext.CurrentHolderId;
            // 走到这里 Content 必不为 null：HandleAsync 开头就挡掉了空信封
            return $"biz:apex:{envelope.Content!.Payload}";
        }

        public override Task<SubscribeResult> ReceiveMessageAsync(NanaEnvelope<TestApexEvent> envelope, CancellationToken cancellationToken = default)
            => Task.FromResult(SubscribeResult.Success());
    }

    /// <summary>重写锁过期时间 —— LockTime 摆什么就用什么</summary>
    public class LockTimeConsumer : VivConsumer<TestApexEvent>
    {
        public TimeSpan LockTime { get; set; } = TimeSpan.FromMinutes(5);

        public LockTimeConsumer(VivConsumerDependency dependency) : base(dependency) { }

        protected override TimeSpan GetConsumeLockTime(NanaEnvelope<TestApexEvent> envelope) => LockTime;

        public override Task<SubscribeResult> ReceiveMessageAsync(NanaEnvelope<TestApexEvent> envelope, CancellationToken cancellationToken = default)
            => Task.FromResult(SubscribeResult.Success());
    }

    /// <summary>
    /// 消费者重试/确认/丢弃语义 —— VivConsumer.HandleAsync 是 Wolverine handler 入口，
    /// 结果映射到框架行为（确认 / 抛 VivRequeueException / 记日志丢弃）。
    /// </summary>
    public class VivConsumerTests
    {
        private static NanaEnvelope<TestApexEvent> Envelope(TestApexEvent? content = null)
            => new() { Content = content ?? new TestApexEvent { Payload = "data" } };

        private static VivConsumerDependency Dep(
            RecordingLogger logger,
            RecordingEventPublisher publisher,
            IOptions<NanaOptions> options,
            IDistributedLock? distributedLock = null,
            RecordingLocalEventBus? localEventBus = null,
            IVivInbox? inbox = null)
            => new(logger, new TestContext(), publisher, options, localEventBus ?? new RecordingLocalEventBus(), distributedLock, inbox: inbox);

        [Fact]
        public async Task 成功_无异常无日志()
        {
            var logger = new RecordingLogger();
            var consumer = new SuccessConsumer(Dep(logger, new RecordingEventPublisher(), XUnitTestMagic.CreateOptions(new NanaOptions())));

            await consumer.HandleAsync(Envelope(), CancellationToken.None);

            Assert.Empty(logger.Errors);
            Assert.Empty(logger.ErrorWithException);
        }

        [Fact]
        public async Task 重投_抛VivRequeueException()
        {
            var consumer = new RequeueConsumer(Dep(new RecordingLogger(), new RecordingEventPublisher(), XUnitTestMagic.CreateOptions(new NanaOptions())));

            var ex = await Assert.ThrowsAsync<VivRequeueException>(() => consumer.HandleAsync(Envelope(), CancellationToken.None));

            Assert.Contains("重投", ex.Message);
        }

        [Fact]
        public async Task 失败不回队_记日志不抛异常()
        {
            var logger = new RecordingLogger();
            var consumer = new DropConsumer(Dep(logger, new RecordingEventPublisher(), XUnitTestMagic.CreateOptions(new NanaOptions())));

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
            var consumer = new SuccessConsumer(Dep(new RecordingLogger(), new RecordingEventPublisher(), XUnitTestMagic.CreateOptions(new NanaOptions())));

            await consumer.HandleAsync(null!, CancellationToken.None);

            Assert.True(true); // 到达这里即未抛异常
        }

        [Fact]
        public async Task 内容为空_直接返回不处理()
        {
            var logger = new RecordingLogger();
            var consumer = new DropConsumer(Dep(logger, new RecordingEventPublisher(), XUnitTestMagic.CreateOptions(new NanaOptions())));

            await consumer.HandleAsync(new NanaEnvelope<TestApexEvent> { Content = null }, CancellationToken.None);

            Assert.Empty(logger.Errors);
        }

        [Fact]
        public async Task 延迟重投_未超上限_投递并计数()
        {
            var logger = new RecordingLogger();
            var publisher = new RecordingEventPublisher();
            var consumer = new RedeliverConsumer(Dep(logger, publisher, XUnitTestMagic.CreateOptions(new NanaOptions() { RetryCount = 3 })));
            var envelope = Envelope();

            await consumer.HandleAsync(envelope, CancellationToken.None);

            Assert.True(publisher.PublishDelayEnvelopeCalled);
            var scheduled = Assert.IsType<NanaEnvelope<TestApexEvent>>(publisher.LastEnvelope);
            Assert.Equal(1, scheduled.ReDeliverCount);          // 原消息 +1，重投副本继承
            Assert.Equal(5, scheduled.DelaySecond);             // DelaySecond 携带延迟值
            Assert.Empty(logger.Errors);
        }

        [Fact]
        public async Task 延迟重投_超上限_丢弃不投递()
        {
            var logger = new RecordingLogger();
            var publisher = new RecordingEventPublisher();
            var consumer = new RedeliverConsumer(Dep(logger, publisher, XUnitTestMagic.CreateOptions(new NanaOptions() { RetryCount = 3 })));
            var envelope = Envelope();
            envelope.ReDeliverCount = 3;                        // 已达上限

            await consumer.HandleAsync(envelope, CancellationToken.None);

            Assert.False(publisher.PublishDelayEnvelopeCalled);
            Assert.Equal(3, envelope.ReDeliverCount);           // 未再自增
            var warning = Assert.Single(logger.Warnings);
            Assert.Contains("上限", warning);
        }

        [Fact]
        public async Task 取到锁_进入业务并释放()
        {
            var logger = new RecordingLogger();
            var distributedLock = new RecordingDistributedLock { AcquireResult = true };
            var consumer = new CountingConsumer(Dep(logger, new RecordingEventPublisher(), XUnitTestMagic.CreateOptions(new NanaOptions()), distributedLock));
            var envelope = Envelope();
            envelope.MessageId = 42;

            await consumer.HandleAsync(envelope, CancellationToken.None);

            Assert.Equal(1, consumer.Calls);
            Assert.Equal(1, distributedLock.AcquireCalls);
            Assert.Equal(1, distributedLock.ReleaseCalls);
            Assert.Equal(NanaRegister.GetConsumerLockKey(nameof(TestApexEvent), 42), distributedLock.LastLockKey);
            Assert.Equal("42", distributedLock.LastHolderId);
            Assert.Empty(logger.Warnings);
        }

        [Fact]
        public async Task 信封带HolderId_取锁用上游持有者()
        {
            var distributedLock = new RecordingDistributedLock { AcquireResult = true };
            var consumer = new CountingConsumer(Dep(new RecordingLogger(), new RecordingEventPublisher(), XUnitTestMagic.CreateOptions(new NanaOptions()), distributedLock));
            var envelope = Envelope();
            envelope.MessageId = 42;
            envelope.Context = new VivContextContent
            {
                AppId = 1,
                UserId = 2,
                TraceId = "client-trace",
                HolderId = "from-publisher"
            };

            await consumer.HandleAsync(envelope, CancellationToken.None);

            Assert.Equal(1, consumer.Calls);
            Assert.Equal("from-publisher", distributedLock.LastHolderId);
        }

        [Fact]
        public async Task 信封无HolderId_不回落TraceId()
        {
            var distributedLock = new RecordingDistributedLock { AcquireResult = true };
            var consumer = new CountingConsumer(Dep(new RecordingLogger(), new RecordingEventPublisher(), XUnitTestMagic.CreateOptions(new NanaOptions()), distributedLock));
            var envelope = Envelope();
            envelope.MessageId = 42;
            envelope.Context = new VivContextContent
            {
                AppId = 1,
                UserId = 2,
                TraceId = "client-trace"
            };

            await consumer.HandleAsync(envelope, CancellationToken.None);

            Assert.Equal("42", distributedLock.LastHolderId);
        }

        [Fact]
        public async Task 默认锁Key_是消息级去重锁()
        {
            var distributedLock = new RecordingDistributedLock { AcquireResult = true };
            var consumer = new CountingConsumer(Dep(new RecordingLogger(), new RecordingEventPublisher(), XUnitTestMagic.CreateOptions(new NanaOptions()), distributedLock));
            var envelope = Envelope();
            envelope.MessageId = 42;

            await consumer.HandleAsync(envelope, CancellationToken.None);

            var expected = NanaRegister.GetConsumerLockKey(typeof(TestApexEvent).Name, 42);
            Assert.Equal(expected, distributedLock.LastLockKey);
            Assert.Equal(expected, distributedLock.LastReleaseKey);
        }

        [Fact]
        public async Task 重写锁Key返回Null_整段锁逻辑跳过()
        {
            var distributedLock = new RecordingDistributedLock();
            var consumer = new NoLockConsumer(Dep(new RecordingLogger(), new RecordingEventPublisher(), XUnitTestMagic.CreateOptions(new NanaOptions()), distributedLock));

            await consumer.HandleAsync(Envelope(), CancellationToken.None);

            Assert.Equal(1, consumer.Calls);
            Assert.Equal(0, distributedLock.AcquireCalls);
            Assert.Equal(0, distributedLock.HeldCalls);
            Assert.Equal(0, distributedLock.ReleaseCalls);
        }

        [Fact]
        public async Task 重写业务锁Key_按业务Key取放且拼Key时读得到本条消息上下文()
        {
            var distributedLock = new RecordingDistributedLock { AcquireResult = true };
            var consumer = new BusinessLockConsumer(Dep(new RecordingLogger(), new RecordingEventPublisher(), XUnitTestMagic.CreateOptions(new NanaOptions()), distributedLock));
            var envelope = Envelope();
            envelope.MessageId = 42;
            envelope.Context = new VivContextContent { AppId = 1, SubjectId = 7, UserId = 2, HolderId = "from-publisher" };

            await consumer.HandleAsync(envelope, CancellationToken.None);

            Assert.Equal("biz:apex:data", distributedLock.LastLockKey);
            Assert.Equal("biz:apex:data", distributedLock.LastReleaseKey);

            // 拼 Key 排在水合之后，才看得到本条消息的租户与持有者
            Assert.Equal(7L, consumer.SeenTenantId);
            Assert.Equal("from-publisher", consumer.SeenHolderId);
        }

        [Fact]
        public async Task 默认锁过期时间_五分钟()
        {
            var distributedLock = new RecordingDistributedLock { AcquireResult = true };
            var consumer = new CountingConsumer(Dep(new RecordingLogger(), new RecordingEventPublisher(), XUnitTestMagic.CreateOptions(new NanaOptions()), distributedLock));

            await consumer.HandleAsync(Envelope(), CancellationToken.None);

            Assert.Equal(TimeSpan.FromMinutes(5), distributedLock.LastExpire);
        }

        [Fact]
        public async Task 重写锁过期时间_按重写值取锁()
        {
            var distributedLock = new RecordingDistributedLock { AcquireResult = true };
            var consumer = new LockTimeConsumer(Dep(new RecordingLogger(), new RecordingEventPublisher(), XUnitTestMagic.CreateOptions(new NanaOptions()), distributedLock))
            {
                LockTime = TimeSpan.FromSeconds(30)
            };

            await consumer.HandleAsync(Envelope(), CancellationToken.None);

            Assert.Equal(TimeSpan.FromSeconds(30), distributedLock.LastExpire);
        }

        [Fact]
        public async Task 重写锁过期时间非正数_回落默认并记Warning()
        {
            // 非正数放任下去是「取锁失败 → 抛 DistributedLockException → 每条消息进死信」，
            // 真实实现对 expire <= 0 不抛不记，错误信息里看不出是过期时间配错了
            var logger = new RecordingLogger();
            var distributedLock = new RecordingDistributedLock { AcquireResult = true };
            var consumer = new LockTimeConsumer(Dep(logger, new RecordingEventPublisher(), XUnitTestMagic.CreateOptions(new NanaOptions()), distributedLock))
            {
                LockTime = TimeSpan.Zero
            };

            await consumer.HandleAsync(Envelope(), CancellationToken.None);

            Assert.Equal(TimeSpan.FromMinutes(5), distributedLock.LastExpire);
            var warning = Assert.Single(logger.Warnings);
            Assert.Contains("过期时间", warning);
        }

        [Fact]
        public async Task 拿不到锁_锁被持有_真竞争丢弃()
        {
            var logger = new RecordingLogger();
            var publisher = new RecordingEventPublisher();
            var distributedLock = new RecordingDistributedLock { AcquireResult = false, IsHeldResult = true };
            var consumer = new CountingConsumer(Dep(logger, publisher, XUnitTestMagic.CreateOptions(new NanaOptions()), distributedLock));

            await consumer.HandleAsync(Envelope(), CancellationToken.None);

            Assert.Equal(0, consumer.Calls);
            Assert.Equal(1, distributedLock.AcquireCalls);
            Assert.Equal(1, distributedLock.HeldCalls);
            Assert.Equal(0, distributedLock.ReleaseCalls);
            Assert.False(publisher.PublishDelayEnvelopeCalled);
            Assert.Empty(logger.Warnings);
            Assert.Empty(logger.Errors);
        }

        [Fact]
        public async Task 取锁失败_锁未被持有_抛异常重试()
        {
            var logger = new RecordingLogger();
            var distributedLock = new RecordingDistributedLock { AcquireResult = false, IsHeldResult = false };
            var consumer = new CountingConsumer(Dep(logger, new RecordingEventPublisher(), XUnitTestMagic.CreateOptions(new NanaOptions()), distributedLock));

            var ex = await Assert.ThrowsAsync<DistributedLockException>(() => consumer.HandleAsync(Envelope(), CancellationToken.None));

            Assert.Equal(0, consumer.Calls);
            Assert.Equal(1, distributedLock.HeldCalls);
            Assert.Equal(0, distributedLock.ReleaseCalls);
            Assert.Contains("锁", ex.Message);
        }

        [Fact]
        public async Task 取锁Redis故障_记Warning并抛DistributedLockException()
        {
            var logger = new RecordingLogger();
            var inner = new VivConnectionException(VivConnType.Redis, "down");
            var distributedLock = new RecordingDistributedLock
            {
                AcquireException = new DistributedLockException("k", 0, inner)
            };
            var consumer = new CountingConsumer(Dep(logger, new RecordingEventPublisher(), XUnitTestMagic.CreateOptions(new NanaOptions()), distributedLock));

            var ex = await Assert.ThrowsAsync<DistributedLockException>(() => consumer.HandleAsync(Envelope(), CancellationToken.None));

            Assert.Equal(0, consumer.Calls);
            Assert.Same(inner, ex.InnerException);
            var warning = Assert.Single(logger.Warnings);
            Assert.Contains("分布式锁服务异常", warning);
        }

        [Fact]
        public async Task 延迟重投_传输失败_异常冒泡不丢弃()
        {
            var publisher = new RecordingEventPublisher
            {
                DelayException = new VivConnectionException(VivConnType.RabbitMQ, "mq down")
            };
            var consumer = new RedeliverConsumer(Dep(new RecordingLogger(), publisher, XUnitTestMagic.CreateOptions(new NanaOptions() { RetryCount = 3 })));

            var ex = await Assert.ThrowsAsync<VivConnectionException>(() => consumer.HandleAsync(Envelope(), CancellationToken.None));

            Assert.Equal(VivConnType.RabbitMQ, ex.ConnType);
            Assert.False(publisher.PublishDelayEnvelopeCalled);
        }

        [Fact]
        public async Task 可选Inbox_重复MessageId第二次TryAccept为false()
        {
            var inbox = new RecordingInbox();
            var consumer = new InboxAwareConsumer(Dep(
                new RecordingLogger(),
                new RecordingEventPublisher(),
                XUnitTestMagic.CreateOptions(new NanaOptions()),
                inbox: inbox));
            var envelope = Envelope();

            await consumer.HandleAsync(envelope, CancellationToken.None);
            await consumer.HandleAsync(envelope, CancellationToken.None);

            Assert.Equal(1, consumer.BusinessCalls);
            Assert.Equal(2, inbox.Attempts.Count);
            Assert.Single(inbox.Accepted);
        }

        [Fact]
        public async Task 可选Inbox_业务键相同但MessageId不同_只处理第一次()
        {
            var inbox = new RecordingInbox();
            var consumer = new BusinessInboxConsumer(Dep(
                new RecordingLogger(),
                new RecordingEventPublisher(),
                XUnitTestMagic.CreateOptions(new NanaOptions()),
                inbox: inbox));

            // 两条不同的消息（MessageId 不同），但说的是同一件事 —— 消息级去重对它们完全无感
            var first = Envelope(new TestApexEvent { Payload = "42" });
            var second = Envelope(new TestApexEvent { Payload = "42" });
            first.MessageId = 100;
            second.MessageId = 200;

            await consumer.HandleAsync(first, CancellationToken.None);
            await consumer.HandleAsync(second, CancellationToken.None);

            Assert.Equal(1, consumer.BusinessCalls);
            Assert.Equal(["biz:order:42", "biz:order:42"], inbox.Attempts);
        }
    }

    /// <summary>按业务键去重 —— 键取自消息内容，两条 MessageId 不同的消息说的是同一件事</summary>
    public class BusinessInboxConsumer : VivConsumer<TestApexEvent>
    {
        public int BusinessCalls { get; private set; }

        public BusinessInboxConsumer(VivConsumerDependency dependency) : base(dependency) { }

        public override async Task<SubscribeResult> ReceiveMessageAsync(
            NanaEnvelope<TestApexEvent> envelope, CancellationToken cancellationToken = default)
        {
            if (!await TryAcceptInboxAsync($"order:{envelope.Content!.Payload}", cancellationToken))
                return SubscribeResult.Success();
            BusinessCalls++;
            return SubscribeResult.Success();
        }
    }

    /// <summary>业务里显式调 Inbox helper；未注入时 TryAcceptInboxAsync 恒为 true。</summary>
    public class InboxAwareConsumer : VivConsumer<TestApexEvent>
    {
        public int BusinessCalls { get; private set; }

        public InboxAwareConsumer(VivConsumerDependency dependency) : base(dependency) { }

        public override async Task<SubscribeResult> ReceiveMessageAsync(
            NanaEnvelope<TestApexEvent> envelope, CancellationToken cancellationToken = default)
        {
            if (!await TryAcceptInboxAsync(envelope, cancellationToken))
                return SubscribeResult.Success();
            BusinessCalls++;
            return SubscribeResult.Success();
        }
    }
}
