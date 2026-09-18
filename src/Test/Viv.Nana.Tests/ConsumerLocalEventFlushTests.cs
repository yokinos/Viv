using Microsoft.Extensions.Options;
using Viv.Contracts.Events;
using Viv.Contracts.Exceptions;
using Viv.Contracts.Interface;
using Viv.Fakes;
using Viv.Nana.Core;
using Viv.Nana.Options;

namespace Viv.Nana.Tests
{
    /// <summary>消费者里入队的本地事件</summary>
    public class FlushTestEvent : LocalEvent
    {
        public string Payload { get; set; } = string.Empty;
    }

    /// <summary>入队一条本地事件，然后按构造时指定的结果返回</summary>
    public class PublishingConsumer : VivConsumer<TestApexEvent>
    {
        private readonly SubscribeResult _result;

        public PublishingConsumer(VivConsumerDependency dependency, SubscribeResult result) : base(dependency)
            => _result = result;

        public override async Task<SubscribeResult> ReceiveMessageAsync(NanaEnvelope<TestApexEvent> envelope, CancellationToken cancellationToken = default)
        {
            await _localEventBus.PublishAsync(new FlushTestEvent { Payload = "data" });
            return _result;
        }
    }

    /// <summary>入队一条本地事件后业务抛异常</summary>
    public class PublishingThrowingConsumer : VivConsumer<TestApexEvent>
    {
        public PublishingThrowingConsumer(VivConsumerDependency dependency) : base(dependency) { }

        public override async Task<SubscribeResult> ReceiveMessageAsync(NanaEnvelope<TestApexEvent> envelope, CancellationToken cancellationToken = default)
        {
            await _localEventBus.PublishAsync(new FlushTestEvent { Payload = "data" });
            throw new InvalidOperationException("消费者炸了");
        }
    }

    /// <summary>本地队列侧的同一个形状</summary>
    public class PublishingLocalConsumer : VivLocalConsumer<LocalTestEvent>
    {
        private readonly SubscribeResult _result;

        public PublishingLocalConsumer(VivLocalConsumerDependency dependency, SubscribeResult result) : base(dependency)
            => _result = result;

        public override async Task<SubscribeResult> ReceiveMessageAsync(NanaLocalEnvelope<LocalTestEvent> envelope, CancellationToken cancellationToken = default)
        {
            await _localEventBus.PublishAsync(new FlushTestEvent { Payload = "data" });
            return _result;
        }
    }

    /// <summary>本地队列：入队后业务抛异常</summary>
    public class PublishingThrowingLocalConsumer : VivLocalConsumer<LocalTestEvent>
    {
        public PublishingThrowingLocalConsumer(VivLocalConsumerDependency dependency) : base(dependency) { }

        public override async Task<SubscribeResult> ReceiveMessageAsync(NanaLocalEnvelope<LocalTestEvent> envelope, CancellationToken cancellationToken = default)
        {
            await _localEventBus.PublishAsync(new FlushTestEvent { Payload = "data" });
            throw new InvalidOperationException("消费者炸了");
        }
    }

    /// <summary>
    /// 消息消费侧的本地事件分发触发点 —— 两个消费者基类的 HandleAsync 都要在 finally 里分发。
    ///
    /// 这是本地事件在 Worker 里唯一的分发入口：没有它，消费者入队的事件只会由 LocalEventBus.Dispose
    /// 记一条「未分发」Warning 整队丢掉 —— 编译通过、运行不报错、事件一条都不发。
    ///
    /// 成功才 Flush、其余一律 Discard，与 HTTP 侧（过滤器/中间件）语义一致。
    /// 两个消费者基类各覆盖一遍：它们是两份独立实现，只测一边另一边照样能漏。
    /// </summary>
    public class ConsumerLocalEventFlushTests
    {
        private static NanaEnvelope<TestApexEvent> Envelope(long messageId = 42)
            => new() { MessageId = messageId, Content = new TestApexEvent { Payload = "data" } };

        private static NanaLocalEnvelope<LocalTestEvent> LocalEnvelope(long messageId = 42)
            => new() { MessageId = messageId, Content = new LocalTestEvent { Payload = "data" } };

        private static VivConsumerDependency Dep(RecordingLocalEventBus bus, IDistributedLock? distributedLock = null)
            => new(
                new RecordingLogger(),
                new TestContext(),
                new RecordingEventPublisher(),
                XUnitTestMagic.CreateOptions(new NanaOptions()),
                bus,
                distributedLock);

        private static VivLocalConsumerDependency LocalDep(RecordingLocalEventBus bus)
            => new(new RecordingLogger(), new TestContext(), new RecordingLocalEventPublisher(), bus);

        // ── 跨进程消费者 ──────────────────────────────────────────

        [Fact]
        public async Task VivConsumer_成功_分发本地事件()
        {
            var bus = new RecordingLocalEventBus();
            var consumer = new PublishingConsumer(Dep(bus), SubscribeResult.Success());

            await consumer.HandleAsync(Envelope(), CancellationToken.None);

            Assert.Equal(1, bus.FlushCalls);
            Assert.Equal(0, bus.DiscardCalls);
            Assert.IsType<FlushTestEvent>(Assert.Single(bus.Published));
        }

        [Fact]
        public async Task VivConsumer_分发不跟停机令牌走()
        {
            // 触发点一律传 CancellationToken.None —— handler 是主业务的一部分，
            // 不因停机/客户端断开而跳过（与 HTTP 侧注释同一条约定）。
            var bus = new RecordingLocalEventBus();

            await new PublishingConsumer(Dep(bus), SubscribeResult.Success()).HandleAsync(Envelope(), CancellationToken.None);

            Assert.Equal(CancellationToken.None, Assert.Single(bus.FlushTokens));
        }

        [Fact]
        public async Task VivConsumer_重投_丢弃且VivRequeueException照旧抛出()
        {
            // Discard 不能顶掉在途的重投异常 —— 否则重投语义被换成一个不相干的异常。
            var bus = new RecordingLocalEventBus();

            var ex = await Assert.ThrowsAsync<VivRequeueException>(
                () => new PublishingConsumer(Dep(bus), SubscribeResult.Failed(true, "重投")).HandleAsync(Envelope(), CancellationToken.None));

            Assert.Contains("重投", ex.Message);
            Assert.Equal(0, bus.FlushCalls);
            Assert.Equal(1, bus.DiscardCalls);
        }

        [Fact]
        public async Task VivConsumer_失败不回队_丢弃()
        {
            var bus = new RecordingLocalEventBus();

            await new PublishingConsumer(Dep(bus), SubscribeResult.Failed(false, "丢弃")).HandleAsync(Envelope(), CancellationToken.None);

            Assert.Equal(0, bus.FlushCalls);
            Assert.Equal(1, bus.DiscardCalls);
        }

        [Fact]
        public async Task VivConsumer_业务抛异常_丢弃且原异常冒泡()
        {
            var bus = new RecordingLocalEventBus();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new PublishingThrowingConsumer(Dep(bus)).HandleAsync(Envelope(), CancellationToken.None));

            Assert.Equal("消费者炸了", ex.Message);
            Assert.Equal(0, bus.FlushCalls);
            Assert.Equal(1, bus.DiscardCalls);
        }

        [Fact]
        public async Task VivConsumer_抢锁失败_丢弃()
        {
            var bus = new RecordingLocalEventBus();
            var distributedLock = new RecordingDistributedLock { AcquireResult = false, IsHeldResult = true };

            await new PublishingConsumer(Dep(bus, distributedLock), SubscribeResult.Success()).HandleAsync(Envelope(), CancellationToken.None);

            // 真竞争丢弃 → 业务没跑、事件也没入队，Discard 是空队列上的 no-op，但路径要对
            Assert.Equal(0, bus.FlushCalls);
            Assert.Equal(1, bus.DiscardCalls);
        }

        [Fact]
        public async Task VivConsumer_空信封_不分发也不丢弃()
        {
            var bus = new RecordingLocalEventBus();

            await new PublishingConsumer(Dep(bus), SubscribeResult.Success()).HandleAsync(null!, CancellationToken.None);

            // 早返在 try 之外，finally 根本不进
            Assert.Empty(bus.Calls);
        }

        [Fact]
        public async Task VivConsumer_分发失败_异常冒泡不被吞()
        {
            // 钉住「handler 抛异常直接上抛」这条语义：本地事件是主业务流的一部分，
            // 静默吞掉就等于「业务以为通知发了、其实没发」。
            var bus = new RecordingLocalEventBus { FlushException = new InvalidOperationException("handler炸了") };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new PublishingConsumer(Dep(bus), SubscribeResult.Success()).HandleAsync(Envelope(), CancellationToken.None));

            Assert.Equal("handler炸了", ex.Message);
        }

        // ── 本地队列消费者 ────────────────────────────────────────

        [Fact]
        public async Task VivLocalConsumer_成功_分发本地事件()
        {
            var bus = new RecordingLocalEventBus();
            var consumer = new PublishingLocalConsumer(LocalDep(bus), SubscribeResult.Success());

            await consumer.HandleAsync(LocalEnvelope(), CancellationToken.None);

            Assert.Equal(1, bus.FlushCalls);
            Assert.Equal(0, bus.DiscardCalls);
            Assert.IsType<FlushTestEvent>(Assert.Single(bus.Published));
        }

        [Fact]
        public async Task VivLocalConsumer_重投_丢弃且VivRequeueException照旧抛出()
        {
            var bus = new RecordingLocalEventBus();

            var ex = await Assert.ThrowsAsync<VivRequeueException>(
                () => new PublishingLocalConsumer(LocalDep(bus), SubscribeResult.Failed(true, "重投")).HandleAsync(LocalEnvelope(), CancellationToken.None));

            Assert.Contains("重投", ex.Message);
            Assert.Equal(0, bus.FlushCalls);
            Assert.Equal(1, bus.DiscardCalls);
        }

        [Fact]
        public async Task VivLocalConsumer_失败不回队_丢弃()
        {
            var bus = new RecordingLocalEventBus();

            await new PublishingLocalConsumer(LocalDep(bus), SubscribeResult.Failed(false, "丢弃")).HandleAsync(LocalEnvelope(), CancellationToken.None);

            Assert.Equal(0, bus.FlushCalls);
            Assert.Equal(1, bus.DiscardCalls);
        }

        [Fact]
        public async Task VivLocalConsumer_业务抛异常_丢弃且原异常冒泡()
        {
            var bus = new RecordingLocalEventBus();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new PublishingThrowingLocalConsumer(LocalDep(bus)).HandleAsync(LocalEnvelope(), CancellationToken.None));

            Assert.Equal("消费者炸了", ex.Message);
            Assert.Equal(0, bus.FlushCalls);
            Assert.Equal(1, bus.DiscardCalls);
        }

        [Fact]
        public async Task VivLocalConsumer_空信封_不分发也不丢弃()
        {
            var bus = new RecordingLocalEventBus();

            await new PublishingLocalConsumer(LocalDep(bus), SubscribeResult.Success()).HandleAsync(null!, CancellationToken.None);

            Assert.Empty(bus.Calls);
        }

        [Fact]
        public async Task VivLocalConsumer_分发失败_异常冒泡不被吞()
        {
            var bus = new RecordingLocalEventBus { FlushException = new InvalidOperationException("handler炸了") };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new PublishingLocalConsumer(LocalDep(bus), SubscribeResult.Success()).HandleAsync(LocalEnvelope(), CancellationToken.None));

            Assert.Equal("handler炸了", ex.Message);
        }
    }
}
