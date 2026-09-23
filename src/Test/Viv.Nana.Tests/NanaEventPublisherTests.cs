using System.Diagnostics;
using Viv.Contracts;
using Viv.Contracts.Enums;
using Viv.Contracts.Exceptions;
using Viv.Contracts.Models;
using Viv.Fakes;
using Viv.Nana.Core;
using Wolverine;

namespace Viv.Nana.Tests
{
    public class NanaEventPublisherTests
    {
        private static NanaEventPublisher Publisher(IMessageBus bus, RecordingLogger? logger = null)
            => new(new TestContext(), bus, logger ?? new RecordingLogger());

        private static IMessageBus ThrowingBus()
            => TestProxy.Create<IMessageBus>(p =>
            {
                p.ThrowOnAnyCall = true;
                p.ThrowException = new InvalidOperationException("broker down");
            });

        [Fact]
        public async Task 内容为null_返回false不调总线()
        {
            var logger = new RecordingLogger();
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
            var logger = new RecordingLogger();
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

        // ── 信封原样重发（供 Viv.Outbox 用）───────────────────────────
        // 与内容版的唯一区别：**不重新盖 holderId**。这条不钉死，发件箱会把「已经冻结在库里的
        // holder 身份」在每次重试时换成一个新值，下游按 holder 做的幂等/追踪就全错位。

        [Fact]
        public async Task 信封为空_返回false不调总线()
        {
            var logger = new RecordingLogger();
            var pub = Publisher(ThrowingBus(), logger);

            Assert.False(await pub.PublishEnvelopeAsync<TestApexEvent>(null!));
            Assert.Empty(logger.ErrorWithException);
        }

        [Fact]
        public async Task 信封内容为null_返回false不调总线()
        {
            var pub = Publisher(ThrowingBus());
            Assert.False(await pub.PublishEnvelopeAsync(new NanaEnvelope<TestApexEvent>()));
        }

        [Fact]
        public async Task 原样重发_保留MessageId与已冻结的holderId()
        {
            var (pub, proxy) = CapturingPublisher();

            LockHolderContext.SetHolderId("holder-of-this-process");
            var envelope = new NanaEnvelope<TestApexEvent>
            {
                Content = new TestApexEvent { Payload = "x" },
                Context = new VivContextContent { SubjectId = 42, HolderId = "holder-frozen-in-db" }
            };
            var originalMessageId = envelope.MessageId;

            Assert.True(await pub.PublishEnvelopeAsync(envelope));

            var sent = Assert.IsType<NanaEnvelope<TestApexEvent>>(proxy.LastArg);
            Assert.Equal(originalMessageId, sent.MessageId);          // 消费端去重键，不能变
            Assert.Equal("holder-frozen-in-db", sent.Context!.HolderId);
            Assert.Equal(42, sent.Context!.SubjectId);
            LockHolderContext.Clear();
        }

        [Fact]
        public async Task 原样重发_信封没holderId也不补当前holder_而内容发布会补()
        {
            var (pub, proxy) = CapturingPublisher();
            LockHolderContext.SetHolderId("holder-of-this-process");

            // 内容版：当场盖章 —— 两个信封都从库里反序列化，Context 里没有 holder
            await pub.PublishAsync(new TestApexEvent { Payload = "content" });
            var fromContent = Assert.IsType<NanaEnvelope<TestApexEvent>>(proxy.LastArg);
            Assert.Equal("holder-of-this-process", fromContent.Context!.HolderId);

            // 信封版：原样透传，不盖章（信封是冻结的，重试不该换身份）
            await pub.PublishEnvelopeAsync(new NanaEnvelope<TestApexEvent>
            {
                Content = new TestApexEvent { Payload = "envelope" },
                Context = new VivContextContent()
            });
            var fromEnvelope = Assert.IsType<NanaEnvelope<TestApexEvent>>(proxy.LastArg);
            Assert.Null(fromEnvelope.Context!.HolderId);

            LockHolderContext.Clear();
        }

        [Fact]
        public async Task 原样重发_总线失败_抛RabbitMQ连接异常()
        {
            var logger = new RecordingLogger();
            var pub = Publisher(ThrowingBus(), logger);
            var envelope = new NanaEnvelope<TestApexEvent> { Content = new TestApexEvent { Payload = "x" } };

            var ex = await Assert.ThrowsAsync<VivConnectionException>(
                async () => await pub.PublishEnvelopeAsync(envelope));

            Assert.Equal(VivConnType.RabbitMQ, ex.ConnType);
            Assert.NotEmpty(logger.ErrorWithException);
        }

        // ── 发布侧 span ───────────────────────────────────────────
        // 消费侧那条线靠 Wolverine 自带的源（Viv.Aspire.ServiceDefaults 里 AddSource("Wolverine")），
        // 发布侧没有，只能自己发。四个发布方法发同一名字，是哪条事件看 event 标签。

        [Fact]
        public async Task 发布_发一条mq_publish_span带event标签()
        {
            var spans = new List<Activity>();
            using var listener = ListenSpans(spans);
            var (pub, _) = CapturingPublisher();

            await pub.PublishAsync(new TestApexEvent { Payload = "x" });

            var span = Assert.Single(spans);

            Assert.Equal(VivTracing.SourceName, span.Source.Name);
            Assert.Equal("mq.publish", span.DisplayName);
            Assert.Equal(ActivityKind.Producer, span.Kind);
            Assert.Equal(nameof(TestApexEvent), span.GetTagItem("event"));

            // 不延迟的不该带这个标签 —— 否则瀑布图上分不出哪些是延迟发的
            Assert.Null(span.GetTagItem("delay.ms"));
        }

        [Fact]
        public async Task 延迟发布_span带上延迟毫秒()
        {
            var spans = new List<Activity>();
            using var listener = ListenSpans(spans);
            var (pub, _) = CapturingPublisher();

            await pub.PublishDelayAsync(TimeSpan.FromSeconds(15), new TestApexEvent { Payload = "x" });

            var span = Assert.Single(spans);
            Assert.Equal("mq.publish", span.DisplayName);
            Assert.Equal(15000d, Assert.IsType<double>(span.GetTagItem("delay.ms")));
        }

        /// <summary>
        /// 有 Activity.Current 时它就该是父 —— 「HTTP 里发的消息挂在同一条 trace 下」全押在这上面。
        /// 写成固定 default 父的话这条会挂，而单看 span 名和标签全都对。
        /// </summary>
        [Fact]
        public async Task 发布_span挂在当前span下面而不是自成一条根()
        {
            var spans = new List<Activity>();
            using var listener = ListenSpans(spans);
            var (pub, _) = CapturingPublisher();

            using var parent = new Activity("http.request").Start();
            await pub.PublishAsync(new TestApexEvent { Payload = "x" });

            var span = Assert.Single(spans);

            Assert.Equal(parent.TraceId.ToString(), span.TraceId.ToString());
            Assert.Equal(parent.SpanId.ToString(), span.ParentSpanId.ToString());
        }

        /// <summary>
        /// 收 <c>VivTracing.Source</c> 发出的 span。必须在调发布**之前**挂上 ——
        /// HasListeners 是在 StartActivity 那一刻读的，挂晚了整条测试会静默变成
        /// 「一条 span 都没有」而不是报错。
        ///
        /// Sample 一定要给：监听者不给采样结论时根本不会建 Activity，同样是静默为空。
        /// </summary>
        private static ActivityListener ListenSpans(List<Activity> spans)
        {
            var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == VivTracing.SourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = spans.Add,
            };

            ActivitySource.AddActivityListener(listener);
            return listener;
        }

        /// <summary>记下最近一条被发布的消息（<see cref="TestProxy.LastArg"/>），并按声明返回类型回一个已完成的结果。</summary>
        private static (NanaEventPublisher Publisher, TestProxy Proxy) CapturingPublisher()
        {
            TestProxy captured = null!;
            var bus = TestProxy.Create<IMessageBus>(p => captured = p);
            return (Publisher(bus), captured);
        }
    }
}
