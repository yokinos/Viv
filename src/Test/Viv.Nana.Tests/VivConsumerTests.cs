using Viv.Contracts.Interface;
using Viv.Log;
using Viv.Nana.Core;

namespace Viv.Nana.Tests
{
    /// <summary>成功消费</summary>
    public class SuccessConsumer : VivConsumer<TestApexEvent>
    {
        public SuccessConsumer(ILoggerContract logger, IVivContext context) : base(logger, context) { }
        public override Task<SubscribeResult> ReceiveMessageAsync(NanaEnvelope<TestApexEvent> message, CancellationToken cancellationToken = default)
            => Task.FromResult(SubscribeResult.Success());
    }

    /// <summary>失败并要求重投（触发 VivRequeueException → Wolverine 重试）</summary>
    public class RequeueConsumer : VivConsumer<TestApexEvent>
    {
        public RequeueConsumer(ILoggerContract logger, IVivContext context) : base(logger, context) { }
        public override Task<SubscribeResult> ReceiveMessageAsync(NanaEnvelope<TestApexEvent> message, CancellationToken cancellationToken = default)
            => Task.FromResult(SubscribeResult.Failed(true, "业务处理失败，重投"));
    }

    /// <summary>失败但不重投（记日志丢弃）</summary>
    public class DropConsumer : VivConsumer<TestApexEvent>
    {
        public DropConsumer(ILoggerContract logger, IVivContext context) : base(logger, context) { }
        public override Task<SubscribeResult> ReceiveMessageAsync(NanaEnvelope<TestApexEvent> message, CancellationToken cancellationToken = default)
            => Task.FromResult(SubscribeResult.Failed(false, "不可重试，丢弃"));
    }

    /// <summary>
    /// 消费者重试/确认/丢弃语义 —— VivConsumer.HandleAsync 是 Wolverine handler 入口，
    /// 结果映射到框架行为（确认 / 抛 VivRequeueException / 记日志丢弃）。
    /// </summary>
    public class VivConsumerTests
    {
        private static NanaEnvelope<TestApexEvent> Envelope(TestApexEvent? content = null)
            => new() { Content = content ?? new TestApexEvent { Payload = "data" } };

        [Fact]
        public async Task 成功_无异常无日志()
        {
            var logger = new StubLogger();
            var context = new FakeContext();

            var consumer = new SuccessConsumer(logger, context);

            await consumer.HandleAsync(Envelope(), CancellationToken.None);

            Assert.Empty(logger.Errors);
            Assert.Empty(logger.ErrorWithException);
        }

        [Fact]
        public async Task 重投_抛VivRequeueException()
        {
            var context = new FakeContext();
            var consumer = new RequeueConsumer(new StubLogger(), context);

            var ex = await Assert.ThrowsAsync<VivRequeueException>(() => consumer.HandleAsync(Envelope(), CancellationToken.None));

            Assert.Contains("重投", ex.Message);
        }

        [Fact]
        public async Task 失败不回队_记日志不抛异常()
        {
            var logger = new StubLogger();
            var context = new FakeContext();
            var consumer = new DropConsumer(logger,context);

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
            var context = new FakeContext();
            var consumer = new SuccessConsumer(new StubLogger(), context);

            await consumer.HandleAsync(null!, CancellationToken.None);

            Assert.True(true); // 到达这里即未抛异常
        }

        [Fact]
        public async Task 内容为空_直接返回不处理()
        {
            var logger = new StubLogger();
            var context = new FakeContext();
            var consumer = new DropConsumer(logger, context);

            await consumer.HandleAsync(new NanaEnvelope<TestApexEvent> { Content = null }, CancellationToken.None);

            Assert.Empty(logger.Errors);
        }
    }
}
