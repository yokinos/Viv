using System.Threading.Tasks;
using Viv.Contracts;
using Viv.Contracts.Exceptions;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Nana.Core;

namespace Viv.Nana.Tests
{
    /// <summary>测试用本地事件</summary>
    public class LocalTestEvent : NanaLocalEvent
    {
        public string Payload { get; set; } = string.Empty;
    }

    /// <summary>
    /// 记录水合与清理调用的上下文桩。
    /// 不能复用 TestSupport 的 FakeContext —— 它的 SetSnapshot 是空实现，验证不了「从信封水合租户」这件事，
    /// 而那正是本地队列唯一的租户隔离防线（消费者跑在后台线程，AsyncLocal 不会跟过去）。
    /// </summary>
    public class RecordingContext : IVivContext
    {
        public VivContextContent? Snapshot { get; private set; }

        public int ClearCalls { get; private set; }

        /// <summary>handler 执行期间读到的 holder（由 RecordingLocalConsumer 填）</summary>
        public string? HolderIdInsideHandler { get; set; }

        public long AppId => Snapshot?.AppId ?? 0;

        public long SubjectId => Snapshot?.SubjectId ?? 0;

        public long UserId => Snapshot?.UserId ?? 0;

        public string TraceId => Snapshot?.TraceId ?? string.Empty;

        public void Clear() => ClearCalls++;

        public VivContextContent? GetRawSnapshot() => Snapshot;

        public void SetSnapshot(VivContextContent model) => Snapshot = model;
    }

    /// <summary>只实现 IVivLocalEventPublisher 的发布器桩（与跨进程的 StubPublisher 互不相干）</summary>
    public class StubLocalPublisher : IVivLocalEventPublisher
    {
        public bool PublishCalled { get; private set; }

        public object? LastContent { get; private set; }

        public bool Result { get; set; } = true;

        public ValueTask<bool> PublishAsync<T>(T content, CancellationToken cancellationToken = default) where T : NanaLocalEvent
        {
            PublishCalled = true;
            LastContent = content;
            return ValueTask.FromResult(Result);
        }

        public ValueTask<bool> PublishDelayAsync<T>(TimeSpan delayTTL, T content, CancellationToken cancellationToken = default) where T : NanaLocalEvent
        {
            PublishCalled = true;
            LastContent = content;
            return ValueTask.FromResult(Result);
        }
    }

    /// <summary>成功消费，并记录进入业务时看到的 holder</summary>
    public class RecordingLocalConsumer : VivLocalConsumer<LocalTestEvent>
    {
        private readonly RecordingContext _recording;

        public RecordingLocalConsumer(VivLocalConsumerDependency dependency, RecordingContext recording) : base(dependency)
            => _recording = recording;

        public override Task<SubscribeResult> ReceiveMessageAsync(NanaLocalEnvelope<LocalTestEvent> envelope, CancellationToken cancellationToken = default)
        {
            _recording.HolderIdInsideHandler = LockHolderContext.CurrentHolderId;
            return Task.FromResult(SubscribeResult.Success());
        }
    }

    /// <summary>失败并要求重投（触发 VivRequeueException → Wolverine 重试）</summary>
    public class RequeueLocalConsumer : VivLocalConsumer<LocalTestEvent>
    {
        public RequeueLocalConsumer(VivLocalConsumerDependency dependency) : base(dependency) { }

        public override Task<SubscribeResult> ReceiveMessageAsync(NanaLocalEnvelope<LocalTestEvent> envelope, CancellationToken cancellationToken = default)
            => Task.FromResult(SubscribeResult.Failed(true, "本地业务处理失败，重投"));
    }

    /// <summary>失败但不重投（记日志丢弃）</summary>
    public class DropLocalConsumer : VivLocalConsumer<LocalTestEvent>
    {
        public DropLocalConsumer(VivLocalConsumerDependency dependency) : base(dependency) { }

        public override Task<SubscribeResult> ReceiveMessageAsync(NanaLocalEnvelope<LocalTestEvent> envelope, CancellationToken cancellationToken = default)
            => Task.FromResult(SubscribeResult.Failed(false, "不可重试，丢弃"));
    }

    /// <summary>业务里直接抛异常</summary>
    public class ThrowingLocalConsumer : VivLocalConsumer<LocalTestEvent>
    {
        public ThrowingLocalConsumer(VivLocalConsumerDependency dependency) : base(dependency) { }

        public override Task<SubscribeResult> ReceiveMessageAsync(NanaLocalEnvelope<LocalTestEvent> envelope, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("消费者炸了");
    }

    /// <summary>
    /// 本地队列消费者语义 —— HandleAsync 是 Wolverine handler 入口，负责
    /// 从信封水合租户上下文、盖 holder、映射消费结果、以及无论如何都清理上下文。
    ///
    /// 与 VivConsumerTests 的对照点：本地队列**没有**消费锁那一段（无取锁、无释放、无锁异常处理）。
    /// </summary>
    public class VivLocalConsumerTests
    {
        private static NanaLocalEnvelope<LocalTestEvent> Envelope(long messageId = 42)
            => new() { MessageId = messageId, Content = new LocalTestEvent { Payload = "data" } };

        private static (VivLocalConsumerDependency Dep, RecordingContext Context, StubLogger Logger) Build()
        {
            var logger = new StubLogger();
            var context = new RecordingContext();
            var publisher = new StubLocalPublisher();
            return (new VivLocalConsumerDependency(logger, context, publisher), context, logger);
        }

        [Fact]
        public async Task 成功_从信封水合租户上下文()
        {
            var (dep, context, logger) = Build();
            var envelope = Envelope();
            envelope.Context = new VivContextContent
            {
                AppId = 1,
                SubjectId = 3,
                UserId = 2,
                TraceId = "client-trace",
                HolderId = "from-publisher"
            };

            await new RecordingLocalConsumer(dep, context).HandleAsync(envelope, CancellationToken.None);

            Assert.Equal(1, context.Snapshot?.AppId);
            Assert.Equal(3, context.Snapshot?.SubjectId);
            Assert.Equal(2, context.Snapshot?.UserId);
            Assert.Equal("client-trace", context.Snapshot?.TraceId);
            Assert.Empty(logger.Errors);
            Assert.Empty(logger.Warnings);
        }

        [Fact]
        public async Task 信封带HolderId_进入业务时用上游持有者()
        {
            var (dep, context, _) = Build();
            var envelope = Envelope();
            envelope.Context = new VivContextContent { AppId = 1, HolderId = "from-publisher" };

            await new RecordingLocalConsumer(dep, context).HandleAsync(envelope, CancellationToken.None);

            Assert.Equal("from-publisher", context.HolderIdInsideHandler);
        }

        [Fact]
        public async Task 信封无HolderId_回落MessageId()
        {
            var (dep, context, _) = Build();
            var envelope = Envelope(42);
            envelope.Context = new VivContextContent { AppId = 1, TraceId = "client-trace" };

            await new RecordingLocalConsumer(dep, context).HandleAsync(envelope, CancellationToken.None);

            // 不回落 TraceId —— 客户端可以伪造 X-Trace-Id 污染锁身份，这条约定与 VivConsumer 一致
            Assert.Equal("42", context.HolderIdInsideHandler);
        }

        [Fact]
        public async Task 信封无上下文_不水合_holder仍回落MessageId()
        {
            var (dep, context, _) = Build();

            await new RecordingLocalConsumer(dep, context).HandleAsync(Envelope(7), CancellationToken.None);

            Assert.Null(context.Snapshot);
            Assert.Equal("7", context.HolderIdInsideHandler);
        }

        // 关于 finally 里的 LockHolderContext.Clear()：从测试外部**观察不到**，故不作断言。
        // 两个原因叠加 ——
        // ① AsyncLocal 的写入只在子异步流内生效，HandleAsync 里 Set/Clear 不会回流到调用方；
        // ② LockHolderContext.CurrentHolderId 的 getter 在值为空时会懒生成一个新 id 并写回，
        //    所以在测试里读它既拿不到 handler 里的值，也拿不到「已被清空」这个状态（只会凭空生成一个）。
        // 能验证的是另一半：finally 确实跑了（ClearCalls）+ handler 内部看到 holder 已被设置（HolderIdInsideHandler）。
        [Fact]
        public async Task 成功_结束后清理上下文()
        {
            var (dep, context, _) = Build();

            await new RecordingLocalConsumer(dep, context).HandleAsync(Envelope(), CancellationToken.None);

            Assert.Equal(1, context.ClearCalls);
        }

        [Fact]
        public async Task 空信封_直接返回不进入业务()
        {
            var (dep, context, _) = Build();

            await new RecordingLocalConsumer(dep, context).HandleAsync(null!, CancellationToken.None);

            Assert.Null(context.HolderIdInsideHandler);
            Assert.Equal(0, context.ClearCalls);
        }

        [Fact]
        public async Task 内容为空_直接返回不进入业务()
        {
            var (dep, context, logger) = Build();

            await new RecordingLocalConsumer(dep, context)
                .HandleAsync(new NanaLocalEnvelope<LocalTestEvent> { Content = null }, CancellationToken.None);

            Assert.Null(context.HolderIdInsideHandler);
            Assert.Empty(logger.Errors);
        }

        [Fact]
        public async Task 重投_抛VivRequeueException且仍清理上下文()
        {
            var (dep, context, _) = Build();

            var ex = await Assert.ThrowsAsync<VivRequeueException>(
                () => new RequeueLocalConsumer(dep).HandleAsync(Envelope(), CancellationToken.None));

            Assert.Contains("重投", ex.Message);
            Assert.Equal(1, context.ClearCalls);
        }

        [Fact]
        public async Task 失败不回队_记日志不抛异常()
        {
            var (dep, context, logger) = Build();

            await new DropLocalConsumer(dep).HandleAsync(Envelope(), CancellationToken.None);

            var error = Assert.Single(logger.Errors);
            Assert.Contains("本地消息消费失败", error);
            Assert.Contains("丢弃", error);
            Assert.Contains("MessageId", error);
            Assert.Equal(1, context.ClearCalls);
        }

        [Fact]
        public async Task 业务抛异常_异常冒泡且仍清理上下文()
        {
            var (dep, context, _) = Build();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new ThrowingLocalConsumer(dep).HandleAsync(Envelope(), CancellationToken.None));

            Assert.Equal("消费者炸了", ex.Message);
            Assert.Equal(1, context.ClearCalls);
        }
    }
}
