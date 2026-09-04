using System.Reflection;
using Viv.Contracts.Enums;
using Viv.Contracts.Exceptions;
using Viv.Nana.Core;
using Wolverine;

namespace Viv.Nana.Tests
{
    public class NanaEventPublisherTests
    {
        private static NanaEventPublisher Publisher(IMessageBus bus, StubLogger? logger = null)
            => new(new FakeContext(), bus, logger ?? new StubLogger());

        private static IMessageBus ThrowingBus()
            => DispatchProxy.Create<IMessageBus, ThrowingMessageBus>();

        [Fact]
        public async Task 内容为null_返回false不调总线()
        {
            var logger = new StubLogger();
            var pub = Publisher(ThrowingBus(), logger);

            Assert.False(await pub.PublishAsync<TestApexEvent>(null!));
            Assert.Empty(logger.ErrorWithException);
        }

        [Fact]
        public async Task 延迟为负_返回false()
        {
            var pub = Publisher(ThrowingBus());
            Assert.False(await pub.PublishDelayAsync(TimeSpan.FromSeconds(-1), new TestApexEvent()));
        }

        [Fact]
        public async Task 信封内容为null_返回false()
        {
            var pub = Publisher(ThrowingBus());
            Assert.False(await pub.PublishDelayAsync(TimeSpan.Zero, new NanaEnvelope<TestApexEvent>()));
        }

        [Fact]
        public async Task 总线失败_抛RabbitMQ连接异常()
        {
            var logger = new StubLogger();
            var pub = Publisher(ThrowingBus(), logger);

            var ex = await Assert.ThrowsAsync<VivConnectionException>(
                async () => await pub.PublishAsync(new TestApexEvent { Payload = "x" }));

            Assert.Equal(VivConnType.RabbitMQ, ex.ConnType);
            Assert.NotEmpty(logger.ErrorWithException);
        }

        [Fact]
        public async Task 调度失败_抛RabbitMQ连接异常()
        {
            var pub = Publisher(ThrowingBus());
            var ex = await Assert.ThrowsAsync<VivConnectionException>(
                async () => await pub.PublishDelayAsync(TimeSpan.FromSeconds(1), new TestApexEvent()));
            Assert.Equal(VivConnType.RabbitMQ, ex.ConnType);
        }

        [Fact]
        public async Task 信封调度失败_抛RabbitMQ连接异常()
        {
            var pub = Publisher(ThrowingBus());
            var envelope = new NanaEnvelope<TestApexEvent> { Content = new TestApexEvent { Payload = "x" } };
            var ex = await Assert.ThrowsAsync<VivConnectionException>(
                async () => await pub.PublishDelayAsync(TimeSpan.FromSeconds(1), envelope));
            Assert.Equal(VivConnType.RabbitMQ, ex.ConnType);
        }

        [Fact]
        public async Task 已取消_抛OperationCanceled()
        {
            var pub = Publisher(ThrowingBus());
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await pub.PublishAsync(new TestApexEvent(), cts.Token));
        }

        private class ThrowingMessageBus : DispatchProxy
        {
            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
                => throw new InvalidOperationException("broker down");
        }
    }
}
