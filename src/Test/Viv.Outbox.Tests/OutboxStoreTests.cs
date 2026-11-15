using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Viv.Contracts;
using Viv.Contracts.Models;
using Viv.Fakes;
using Viv.Momo;
using Viv.Nana;
using Viv.Outbox.Core;

namespace Viv.Outbox.Tests;

/// <summary>
/// 写路径：<see cref="OutboxStore.EnqueueAsync"/> 只落库、不发消息。
/// 「不发消息」在这里是**结构上**保证的（这个类根本拿不到发布器），测试用反射把它钉死。
///
/// 入队要求「已经在事务里」（<see cref="IMomoDbContext.IsInTransaction"/>）—— 这是发件箱唯一
/// 价值的前提，所以正常路径的替身必须把这一项配成 true，另有一条用例专门钉住「不在事务里就抛」。
/// </summary>
public class OutboxStoreTests
{
    /// <summary>探针等待时长。只用来区分「立刻被叫醒」和「没人叫」，不必等到兜底间隔</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(80);

    private static (OutboxStore Store, StubOutboxRepository Repo, TestContext Context, RecordingLogger Logger) Build(
        TimeProvider? time = null)
    {
        var repo = new StubOutboxRepository();
        var context = new TestContext();
        var logger = new RecordingLogger();
        return (new OutboxStore(repo, InTransaction(), context, logger, new OutboxCommitObserver(new OutboxSignal()), time), repo, context, logger);
    }

    /// <summary>「正处于业务事务中」的库上下文替身。<c>IMomoDbContext</c> 有五十多个成员，用代理而不是手写。</summary>
    private static IMomoDbContext InTransaction()
        => TestProxy.Create<IMomoDbContext>(proxy => proxy.Returns[typeof(bool)] = true);

    /// <summary>「没开事务」的库上下文替身 —— 代理由默认值给出 <c>IsInTransaction = false</c>。</summary>
    private static IMomoDbContext NoTransaction() => TestProxy.Create<IMomoDbContext>();

    [Fact]
    public async Task 不在事务中入队_直接抛且不落行()
    {
        var repo = new StubOutboxRepository();
        // 默认回值就是 false —— 没开事务的那个现场
        var db = TestProxy.Create<IMomoDbContext>();
        var store = new OutboxStore(repo, db, new TestContext(), new RecordingLogger(), new OutboxCommitObserver(new OutboxSignal()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.EnqueueAsync(new OutboxTestEvent { Payload = "p" }));

        // 断言要能把人指到修法上，只说「失败」等于没说话
        Assert.Contains("事务", ex.Message);
        Assert.Empty(repo.Inserted);
    }

    [Fact]
    public async Task 入队时间列取自注入时钟()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var (store, repo, _, _) = Build(time);

        await store.EnqueueAsync(new OutboxTestEvent { Payload = "p" });

        var row = Assert.Single(repo.Inserted);
        Assert.Equal(time.GetUtcNow().UtcDateTime, row.OccurredAt);
        Assert.Equal(row.OccurredAt, row.NextRetryAt);

        // Kind=Utc 是硬要求：PG 侧是 TIMESTAMPTZ，Npgsql 拒绝写 Kind=Unspecified
        Assert.Equal(DateTimeKind.Utc, row.OccurredAt.Kind);
    }

    [Fact]
    public async Task 入队_落一行Pending_不发任何消息()
    {
        var (store, repo, _, _) = Build();

        Assert.True(await store.EnqueueAsync(new OutboxTestEvent { Payload = "p" }));

        var row = Assert.Single(repo.Inserted);
        Assert.Equal(OutboxStatus.Pending, row.Status);
        Assert.Equal(0, row.RetryCount);
        Assert.NotEqual(0, row.Id);
        Assert.NotEqual(0, row.MessageId);
        Assert.Null(row.LeaseUntil);
        Assert.Null(row.SentAt);
        Assert.Null(row.LastError);
        Assert.Equal(row.OccurredAt, row.NextRetryAt);
    }

    [Fact]
    public async Task 入队_EventType存FullName而非程序集限定名()
    {
        var (store, repo, _, _) = Build();

        await store.EnqueueAsync(new OutboxTestEvent());

        var row = Assert.Single(repo.Inserted);
        Assert.Equal(typeof(OutboxTestEvent).FullName, row.EventType);

        // AQN 里带程序集版本号 —— 存它的话，一次发版就会让库里旧行的类型解析不出来
        Assert.DoesNotContain(", Version=", row.EventType);
        Assert.DoesNotContain(", Viv.Outbox.Tests", row.EventType);
    }

    [Fact]
    public async Task 入队_Payload能解回原事件_且MessageId与列一致()
    {
        var (store, repo, _, _) = Build();

        await store.EnqueueAsync(new OutboxTestEvent { Payload = "hello", Number = 42 });

        var row = Assert.Single(repo.Inserted);
        var envelope = JsonSerializer.Deserialize<NanaEnvelope<OutboxTestEvent>>(row.Payload, TestPayload.JsonOptions);

        Assert.NotNull(envelope);
        Assert.Equal("hello", envelope.Content!.Payload);
        Assert.Equal(42, envelope.Content.Number);
        Assert.Equal(row.MessageId, envelope.MessageId);
    }

    [Fact]
    public async Task 入队_带上下文时_租户信息原样进payload()
    {
        var (store, repo, context, _) = Build();
        context.Snapshot = new VivContextContent { AppId = 9, SubjectId = 42, UserId = 7, TraceId = "t-1" };

        await store.EnqueueAsync(new OutboxTestEvent());

        var row = Assert.Single(repo.Inserted);
        var envelope = JsonSerializer.Deserialize<NanaEnvelope<OutboxTestEvent>>(row.Payload, TestPayload.JsonOptions)!;

        Assert.Equal(9, envelope.Context!.AppId);
        Assert.Equal(42, envelope.Context.SubjectId);
        Assert.Equal(7, envelope.Context.UserId);
        Assert.Equal("t-1", envelope.Context.TraceId);
    }

    [Fact]
    public async Task 入队_上下文已有holderId_不被当前holder覆盖()
    {
        var (store, repo, context, _) = Build();
        context.Snapshot = new VivContextContent { SubjectId = 1, HolderId = "upstream-holder" };
        LockHolderContext.SetHolderId("local-holder");
        try
        {
            await store.EnqueueAsync(new OutboxTestEvent());

            var row = Assert.Single(repo.Inserted);
            var envelope = JsonSerializer.Deserialize<NanaEnvelope<OutboxTestEvent>>(row.Payload, TestPayload.JsonOptions)!;
            Assert.Equal("upstream-holder", envelope.Context!.HolderId);
        }
        finally
        {
            LockHolderContext.Clear();
        }
    }

    [Fact]
    public async Task 入队_无上下文时_兜底成空上下文并盖当前holder()
    {
        var (store, repo, _, _) = Build();
        LockHolderContext.SetHolderId("local-holder");
        try
        {
            await store.EnqueueAsync(new OutboxTestEvent());

            var row = Assert.Single(repo.Inserted);
            var envelope = JsonSerializer.Deserialize<NanaEnvelope<OutboxTestEvent>>(row.Payload, TestPayload.JsonOptions)!;
            Assert.Equal("local-holder", envelope.Context!.HolderId);
            Assert.Equal(0, envelope.Context.SubjectId);
        }
        finally
        {
            LockHolderContext.Clear();
        }
    }

    [Fact]
    public async Task 入队_克隆上下文_不把业务侧的快照对象共享出去()
    {
        var (store, repo, context, _) = Build();
        context.Snapshot = new VivContextContent { SubjectId = 42 };

        await store.EnqueueAsync(new OutboxTestEvent());

        // 入队后请求继续跑、快照可能被改 —— payload 里的必须是入队那一刻的副本
        context.Snapshot.SubjectId = 99;

        var row = Assert.Single(repo.Inserted);
        var envelope = JsonSerializer.Deserialize<NanaEnvelope<OutboxTestEvent>>(row.Payload, TestPayload.JsonOptions)!;
        Assert.Equal(42, envelope.Context!.SubjectId);
    }

    // ── 溯源两列 ──────────────────────────────────────────────
    // 投递器跑在后台作用域里，Activity.Current 是 null、请求上下文也早没了，
    // 入队那一刻不存下来就永久丢了。这两条钉的就是「真的存了」。

    [Fact]
    public async Task 入队_记下当前span的TraceId与请求TraceId()
    {
        var (store, repo, context, _) = Build();
        context.Snapshot = new VivContextContent { SubjectId = 42, TraceId = "0HN7AAA:00000001" };

        using var activity = new Activity("test").Start();

        await store.EnqueueAsync(new OutboxTestEvent());

        var row = Assert.Single(repo.Inserted);

        // OTel 那条：dashboard 上给你的 traceId，W3C 格式 32 位十六进制
        Assert.Equal(activity.TraceId.ToString(), row.TraceId);
        Assert.Equal(32, row.TraceId!.Length);

        // 请求那条：HttpContext.TraceIdentifier，也是响应体 traceId 字段里那个
        Assert.Equal("0HN7AAA:00000001", row.RequestTraceId);
    }

    [Fact]
    public async Task 入队_没有span也没有请求上下文时_两列存NULL而不是空串()
    {
        var (store, repo, _, _) = Build();

        // Worker / 后台任务里入队就是这个样子：Activity.Current 为 null，
        // TestContext 无快照 → IVivContext.TraceId 给的是空串。
        // 存空串的话库里长得像「有但是空的」，NULL 才是「本来就没有」
        Assert.Null(Activity.Current);

        await store.EnqueueAsync(new OutboxTestEvent());

        var row = Assert.Single(repo.Inserted);
        Assert.Null(row.TraceId);
        Assert.Null(row.RequestTraceId);
    }

    [Fact]
    public async Task 内容为null_返回false且不落库()
    {
        var (store, repo, _, logger) = Build();

        Assert.False(await store.EnqueueAsync<OutboxTestEvent>(null!));

        Assert.Empty(repo.Inserted);
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public async Task 落库0行_返回false并记Error()
    {
        var (store, repo, _, logger) = Build();
        repo.InsertResult = false;

        Assert.False(await store.EnqueueAsync(new OutboxTestEvent()));

        var error = Assert.Single(logger.Errors);
        Assert.Contains("入队未生效", error);
    }

    [Fact]
    public async Task 两次入队的Id与MessageId互不相同()
    {
        var (store, repo, _, _) = Build();

        await store.EnqueueAsync(new OutboxTestEvent { Number = 1 });
        await store.EnqueueAsync(new OutboxTestEvent { Number = 2 });

        Assert.Equal(2, repo.Inserted.Count);
        Assert.Equal(2, repo.Inserted.Select(x => x.Id).Distinct().Count());
        Assert.Equal(2, repo.Inserted.Select(x => x.MessageId).Distinct().Count());
    }

    // ── 提交后唤醒投递器 ──────────────────────────────────────
    // 入队本身不能叫醒投递器 —— 行还在未提交的事务里，醒了也认领不到，白跑一轮继续睡。
    // 所以这里只打标记，由 Momo 在提交成功后回调 OnCommitted 才转成一次 Notify。
    // 这三条钉的是「谁在什么时候叫醒投递器」，改错任何一处都表现为「偶尔莫名慢 5 秒」。

    [Fact]
    public async Task 入队后未提交_不唤醒投递器()
    {
        var signal = new OutboxSignal();
        var observer = new OutboxCommitObserver(signal);
        var store = new OutboxStore(new StubOutboxRepository(), InTransaction(), new TestContext(), new RecordingLogger(), observer);

        await store.EnqueueAsync(new OutboxTestEvent());

        // 还在事务里：此刻叫醒投递器等于让它白跑一轮
        Assert.False(await signal.WaitAsync(ProbeTimeout, TimeProvider.System, CancellationToken.None));
    }

    [Fact]
    public async Task 入队后提交_唤醒投递器()
    {
        var signal = new OutboxSignal();
        var observer = new OutboxCommitObserver(signal);
        var store = new OutboxStore(new StubOutboxRepository(), InTransaction(), new TestContext(), new RecordingLogger(), observer);

        await store.EnqueueAsync(new OutboxTestEvent());
        observer.OnCommitted();

        Assert.True(await signal.WaitAsync(ProbeTimeout, TimeProvider.System, CancellationToken.None));
    }

    [Fact]
    public async Task 入队没生效_提交也不唤醒投递器()
    {
        var signal = new OutboxSignal();
        var observer = new OutboxCommitObserver(signal);
        var repo = new StubOutboxRepository { InsertResult = false };
        var store = new OutboxStore(repo, InTransaction(), new TestContext(), new RecordingLogger(), observer);

        Assert.False(await store.EnqueueAsync(new OutboxTestEvent()));
        observer.OnCommitted();

        // 一行都没落，叫醒投递器只会换来一轮空认领
        Assert.False(await signal.WaitAsync(ProbeTimeout, TimeProvider.System, CancellationToken.None));
    }

    // ── 不要事务的入口 ────────────────────────────────────────
    // 与事务版的差别只有两处：开头不校验事务、无事务时落库即唤醒。
    // 前者写错会让原子性无声消失，后者写漏会让这条路径退回 5 秒延迟 —— 两条都钉在这里。

    [Fact]
    public async Task 不要事务的入队_不在事务中也照常落库()
    {
        var repo = new StubOutboxRepository();
        var store = new OutboxStore(repo, NoTransaction(), new TestContext(), new RecordingLogger(), new OutboxCommitObserver(new OutboxSignal()));

        Assert.True(await store.EnqueueWithoutTransactionAsync(new OutboxTestEvent { Payload = "p" }));

        var row = Assert.Single(repo.Inserted);
        Assert.Equal(OutboxStatus.Pending, row.Status);
        Assert.Equal(0, row.RetryCount);
    }

    [Fact]
    public async Task 不要事务的入队_落库即唤醒投递器()
    {
        var signal = new OutboxSignal();
        var observer = new OutboxCommitObserver(signal);
        var store = new OutboxStore(new StubOutboxRepository(), NoTransaction(), new TestContext(), new RecordingLogger(), observer);

        await store.EnqueueWithoutTransactionAsync(new OutboxTestEvent());

        // 语句自动提交：InsertAsync 返回时行已可见，没有"提交"可等，
        // 不该让投递器干等一个兜底间隔
        Assert.True(await signal.WaitAsync(ProbeTimeout, TimeProvider.System, CancellationToken.None));
    }

    [Fact]
    public async Task 不要事务的入队_恰好有事务时仍等提交才唤醒()
    {
        var signal = new OutboxSignal();
        var observer = new OutboxCommitObserver(signal);
        var store = new OutboxStore(new StubOutboxRepository(), InTransaction(), new TestContext(), new RecordingLogger(), observer);

        await store.EnqueueWithoutTransactionAsync(new OutboxTestEvent());

        // 入口名说的是"不要求事务"，不是"不许有事务"。外层真开了事务，
        // 这条消息就跟着一起提交 —— 提前叫醒等于认领一个还看不见的行
        Assert.False(await signal.WaitAsync(ProbeTimeout, TimeProvider.System, CancellationToken.None));

        observer.OnCommitted();
        Assert.True(await signal.WaitAsync(ProbeTimeout, TimeProvider.System, CancellationToken.None));
    }

    [Fact]
    public async Task 不要事务的入队_记一条Warning说明放弃了原子性()
    {
        var logger = new RecordingLogger();
        var store = new OutboxStore(new StubOutboxRepository(), NoTransaction(), new TestContext(), logger, new OutboxCommitObserver(new OutboxSignal()));

        await store.EnqueueWithoutTransactionAsync(new OutboxTestEvent());

        // 它不是错误（调用方是知情地选的），但必须留下痕迹：
        // 「消息发了、业务没成」事后对账时，这条日志是唯一能解释原因的线索
        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("未参与事务", warning);
        Assert.Contains(nameof(IVivOutbox.EnqueueAsync), warning);
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public async Task 不要事务的入队_内容为null_返回false且不落库()
    {
        var repo = new StubOutboxRepository();
        var store = new OutboxStore(repo, NoTransaction(), new TestContext(), new RecordingLogger(), new OutboxCommitObserver(new OutboxSignal()));

        Assert.False(await store.EnqueueWithoutTransactionAsync<OutboxTestEvent>(null!));

        Assert.Empty(repo.Inserted);
    }

    [Fact]
    public void 入队器不依赖发布器_结构上就不可能当场发消息()
    {
        var dependencies = typeof(OutboxStore)
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .ToList();

        // 「入队 ≠ 发送」是这个模式的地基。注入发布器 = 有人想在这里顺手发一条。
        Assert.DoesNotContain(typeof(IVivEventPublisher), dependencies);

        // 顺带把服务定位器这个后门也堵上 —— 给了 IServiceProvider 一样能绕回去解析发布器
        Assert.DoesNotContain(typeof(IServiceProvider), dependencies);
    }
}
