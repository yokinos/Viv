using Microsoft.Extensions.DependencyInjection;
using Viv.Contracts;
using Viv.Fakes;
using Viv.Nana;
using Viv.Outbox.Core;
using Viv.Outbox.Options;

namespace Viv.Outbox.Tests;

/// <summary>
/// 投递器的一轮：释放过期租约 → 排空认领 + 投递 → 清理。
/// 只在窄接口 <see cref="IOutboxRepository"/> 上测，不碰数据库。
/// </summary>
public class OutboxWorkerTests
{
    private static string EventTypeName => typeof(OutboxTestEvent).FullName!;

    private static OutboxMessage Row(long id, string? eventType = null, int retryCount = 0)
    {
        var messageId = id * 10;
        return new OutboxMessage
        {
            Id = id,
            MessageId = messageId,
            EventType = eventType ?? EventTypeName,
            Payload = TestPayload.For(new OutboxTestEvent { Payload = $"p-{id}", Number = (int)id }, messageId),
            Status = OutboxStatus.Processing,
            RetryCount = retryCount,
            NextRetryAt = DateTime.UtcNow,
            OccurredAt = DateTime.UtcNow,
        };
    }

    private static OutboxOptions DefaultOptions() => new()
    {
        AutoCreateTable = true,
        BatchSize = 100,
        MaxRetryCount = 3,
        LeaseSeconds = 60,
        RetentionDays = 7,
    };

    private static OutboxWorker Build(
        StubOutboxRepository repo,
        RecordingEventPublisher publisher,
        RecordingLogger logger,
        OutboxOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<IOutboxRepository>(_ => repo);
        services.AddScoped<IVivEventPublisher>(_ => publisher);
        var provider = services.BuildServiceProvider();

        return new OutboxWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new OutboxEnvelopeSenderFactory(),
            logger,
            options ?? DefaultOptions());
    }

    [Fact]
    public async Task 投递成功_标记Sent()
    {
        var repo = new StubOutboxRepository();
        var publisher = new RecordingEventPublisher();
        var logger = new RecordingLogger();
        repo.ClaimScript.Enqueue(new List<OutboxMessage> { Row(1) });

        var delivered = await Build(repo, publisher, logger).RunOnceAsync();

        Assert.Equal(1, delivered);
        Assert.Equal(1, Assert.Single(repo.Sent).Id);
        Assert.Empty(repo.Pending);
        Assert.Empty(repo.Failed);

        var envelope = publisher.Last<OutboxTestEvent>();
        Assert.NotNull(envelope);
        Assert.Equal(10, envelope.MessageId);
        Assert.Equal("p-1", envelope.Content!.Payload);
    }

    [Fact]
    public async Task 投递失败_退回Pending并按退避推后NextRetryAt()
    {
        var repo = new StubOutboxRepository();
        var publisher = new RecordingEventPublisher { PublishException = new Exception("MQ 挂了") };
        var logger = new RecordingLogger();
        repo.ClaimScript.Enqueue(new List<OutboxMessage> { Row(1, retryCount: 0) });

        var before = DateTime.UtcNow;
        await Build(repo, publisher, logger).RunOnceAsync();

        var pending = Assert.Single(repo.Pending);
        Assert.Equal(1, pending.Id);
        Assert.Equal(1, pending.RetryCount);
        Assert.Equal("MQ 挂了", pending.LastError);
        Assert.Empty(repo.Sent);
        Assert.Empty(repo.Failed);

        // 退避第一档 5s 起（含抖动）—— 不能立刻重试，那会变成忙等
        Assert.True(pending.NextRetryAt >= before.AddSeconds(4), $"NextRetryAt 太早：{pending.NextRetryAt:O}");

        Assert.Single(logger.Warnings);
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public async Task 重试累加_每条消息各自算自己的次数()
    {
        var repo = new StubOutboxRepository();
        var publisher = new RecordingEventPublisher { PublishException = new Exception("boom") };
        // MaxRetryCount = 3：两条都还没到顶，本次各 +1
        repo.ClaimScript.Enqueue(new List<OutboxMessage> { Row(1, retryCount: 1), Row(2, retryCount: 0) });

        await Build(repo, publisher, new RecordingLogger()).RunOnceAsync();

        Assert.Equal(2, repo.Pending.Single(x => x.Id == 1).RetryCount);
        Assert.Equal(1, repo.Pending.Single(x => x.Id == 2).RetryCount);
    }

    [Fact]
    public async Task 重试达上限_置Failed不再回队()
    {
        var repo = new StubOutboxRepository();
        var publisher = new RecordingEventPublisher { PublishException = new Exception("一直失败") };
        var logger = new RecordingLogger();
        // MaxRetryCount = 3，这条已经失败过 2 次 → 本次是第 3 次，到顶
        repo.ClaimScript.Enqueue(new List<OutboxMessage> { Row(1, retryCount: 2) });

        await Build(repo, publisher, logger).RunOnceAsync();

        var failed = Assert.Single(repo.Failed);
        Assert.Equal(3, failed.RetryCount);
        Assert.Equal("一直失败", failed.LastError);
        Assert.Empty(repo.Pending);

        // 耗尽重试必须吵 —— 它已经永远不会自己发出去了
        Assert.Single(logger.Errors);
    }

    [Fact]
    public async Task 未知事件类型_置Failed并记Error_不静默丢()
    {
        var repo = new StubOutboxRepository();
        var publisher = new RecordingEventPublisher();
        var logger = new RecordingLogger();
        repo.ClaimScript.Enqueue(new List<OutboxMessage> { Row(1, eventType: "Viv.Nowhere.NoSuchEvent") });

        await Build(repo, publisher, logger).RunOnceAsync();

        var failed = Assert.Single(repo.Failed);
        Assert.Contains("Viv.Nowhere.NoSuchEvent", failed.LastError);

        // 类型解析不出来 = 重试一万次也一样，不能当普通失败去消耗重试次数
        Assert.Empty(repo.Pending);
        Assert.Empty(repo.Sent);
        Assert.Empty(publisher.Envelopes);

        var error = Assert.Single(logger.Errors);
        Assert.Contains("解析不出来", error);
    }

    [Fact]
    public async Task 未知事件类型_不消耗重试次数()
    {
        var repo = new StubOutboxRepository();
        repo.ClaimScript.Enqueue(new List<OutboxMessage> { Row(1, eventType: "Viv.Nowhere.NoSuchEvent", retryCount: 0) });

        await Build(repo, new RecordingEventPublisher(), new RecordingLogger()).RunOnceAsync();

        // 原样带过去，不 +1
        Assert.Equal(0, Assert.Single(repo.Failed).RetryCount);
    }

    [Fact]
    public async Task 一轮先释放过期租约_再认领()
    {
        var repo = new StubOutboxRepository();

        await Build(repo, new RecordingEventPublisher(), new RecordingLogger()).RunOnceAsync();

        // 顺序不能反：先认领再释放的话，崩溃遗留的行要等下一轮才复活
        Assert.Equal(1, repo.ReleaseExpiredCalls);
        Assert.Equal(1, repo.ClaimCalls);
    }

    [Fact]
    public async Task 排空_一次轮询连续认领直到认空()
    {
        var repo = new StubOutboxRepository();
        var options = DefaultOptions();
        options.BatchSize = 2;
        // 第一批认满 → 继续；第二批不足一批 → 收工
        repo.ClaimScript.Enqueue(new List<OutboxMessage> { Row(1), Row(2) });
        repo.ClaimScript.Enqueue(new List<OutboxMessage> { Row(3) });

        var delivered = await Build(repo, new RecordingEventPublisher(), new RecordingLogger(), options).RunOnceAsync();

        Assert.Equal(3, delivered);
        Assert.Equal(2, repo.ClaimCalls);
        Assert.Equal(3, repo.Sent.Count);
    }

    [Fact]
    public async Task 认出0行_立即结束本轮()
    {
        var repo = new StubOutboxRepository();

        var delivered = await Build(repo, new RecordingEventPublisher(), new RecordingLogger()).RunOnceAsync();

        Assert.Equal(0, delivered);
        Assert.Equal(1, repo.ClaimCalls);
        Assert.Empty(repo.Sent);
    }

    [Fact]
    public async Task 认领参数_按配置给批大小与租约()
    {
        var repo = new StubOutboxRepository();
        var options = DefaultOptions();
        options.BatchSize = 25;
        options.LeaseSeconds = 90;

        await Build(repo, new RecordingEventPublisher(), new RecordingLogger(), options).RunOnceAsync();

        Assert.Equal(25, repo.LastClaimBatchSize);
        Assert.NotNull(repo.LastClaimNow);
        Assert.NotNull(repo.LastClaimLeaseUntil);
        Assert.Equal(90, (repo.LastClaimLeaseUntil!.Value - repo.LastClaimNow!.Value).TotalSeconds, 1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task 租约配成非正数_兜底成1秒而不是零(int leaseSeconds)
    {
        var repo = new StubOutboxRepository();
        var options = DefaultOptions();
        options.LeaseSeconds = leaseSeconds;

        await Build(repo, new RecordingEventPublisher(), new RecordingLogger(), options).RunOnceAsync();

        // 租约 0 秒 = 认领的瞬间就过期，多个实例会把同一条消息翻来覆去地投
        Assert.Equal(1, (repo.LastClaimLeaseUntil!.Value - repo.LastClaimNow!.Value).TotalSeconds, 1);
    }

    [Fact]
    public async Task 启动_建表一次并打启动日志()
    {
        var repo = new StubOutboxRepository();
        var logger = new RecordingLogger();

        Assert.True(await Build(repo, new RecordingEventPublisher(), logger).StartupAsync());

        Assert.Equal(1, repo.EnsureTableCalls);
        Assert.Single(logger.Infos);
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public async Task 启动_关掉自动建表就不建()
    {
        var repo = new StubOutboxRepository();
        var options = DefaultOptions();
        options.AutoCreateTable = false;

        Assert.True(await Build(repo, new RecordingEventPublisher(), new RecordingLogger(), options).StartupAsync());

        Assert.Equal(0, repo.EnsureTableCalls);
    }

    [Fact]
    public async Task 启动_建表失败返回false_投递器不再空转()
    {
        var repo = new StubOutboxRepository { EnsureTableException = new Exception("表建不出来") };
        var logger = new RecordingLogger();

        // 表都建不出来还继续跑，只会每轮刷一条一模一样的错误，把真正的问题埋掉
        Assert.False(await Build(repo, new RecordingEventPublisher(), logger).StartupAsync());

        Assert.Single(logger.Errors);
    }

    [Fact]
    public async Task 清理_保留期非正数_不清理()
    {
        var repo = new StubOutboxRepository();
        var options = DefaultOptions();
        options.RetentionDays = 0;

        await Build(repo, new RecordingEventPublisher(), new RecordingLogger(), options).RunOnceAsync();

        Assert.Equal(0, repo.CleanupCalls);
    }

    [Fact]
    public async Task 清理_删到没得删为止()
    {
        var repo = new StubOutboxRepository { CleanupResult = false };

        await Build(repo, new RecordingEventPublisher(), new RecordingLogger()).RunOnceAsync();

        Assert.Equal(1, repo.CleanupCalls);
    }

    [Fact]
    public async Task 清理_单轮有批数上限_删不完留给下一轮且说出来()
    {
        var repo = new StubOutboxRepository { CleanupResult = true };
        var logger = new RecordingLogger();

        await Build(repo, new RecordingEventPublisher(), logger).RunOnceAsync();

        // 保留期配错时不能把一整轮（乃至整个进程）耗在清理上
        Assert.Equal(50, repo.CleanupCalls);

        // 不静默截断
        Assert.Contains(logger.Infos, x => x.Contains("上限"));
    }

    [Fact]
    public async Task 本轮异常向上冒泡_由调度层去吞()
    {
        var repo = new StubOutboxRepository { ClaimException = new InvalidOperationException("认领炸了") };

        // 这一层负责**报错**，不负责吞 —— 吞异常是 OutboxDispatcher 的职责
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await Build(repo, new RecordingEventPublisher(), new RecordingLogger()).RunOnceAsync());
    }

    [Fact]
    public void 退避_5秒起指数翻倍_封顶60秒加抖动()
    {
        for (var retryCount = 1; retryCount <= 12; retryCount++)
        {
            var floor = Math.Min(5000.0 * Math.Pow(2, retryCount - 1), 60000.0);
            var actual = OutboxWorker.Backoff(retryCount).TotalMilliseconds;

            // 下界保证「退得够开」，上界保证不会退到天荒地老（+30% 抖动）
            Assert.InRange(actual, floor, floor * 1.3);
        }
    }

    [Fact]
    public void 退避_次数给0或负数也不炸()
    {
        Assert.InRange(OutboxWorker.Backoff(0).TotalSeconds, 5, 6.5);
        Assert.InRange(OutboxWorker.Backoff(-3).TotalSeconds, 5, 6.5);
    }

    [Fact]
    public async Task 停机取消_不消耗重试次数_把OCE上抛给宿主()
    {
        var repo = new StubOutboxRepository();
        // 停机时发布器抛 OCE —— 这条路径必须走「上抛」而不是「当失败重试」：
        // 停机不消耗重试次数，租约到期后这条自然会被重新认领。
        var publisher = new RecordingEventPublisher { PublishException = new OperationCanceledException() };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await Build(repo, publisher, new RecordingLogger()).DeliverAsync(repo, publisher, Row(1), cts.Token));

        Assert.Empty(repo.Pending);
        Assert.Empty(repo.Failed);
        Assert.Empty(repo.Sent);
    }

    [Fact]
    public async Task 投递_信封里的holder原样送达_不被当前holder覆盖()
    {
        var repo = new StubOutboxRepository();
        var publisher = new RecordingEventPublisher();

        var row = Row(1);
        // 把**发布那一刻**盖章过的 holder 写进 payload（OutboxStore 干的就是这件事）
        row.Payload = TestPayload.For(
            new OutboxTestEvent { Payload = "p-1" },
            row.MessageId,
            new Viv.Contracts.Models.VivContextContent { HolderId = "holder-at-publish" });
        repo.ClaimScript.Enqueue(new List<OutboxMessage> { row });

        LockHolderContext.SetHolderId("holder-now");
        try
        {
            await Build(repo, publisher, new RecordingLogger()).RunOnceAsync();
        }
        finally
        {
            LockHolderContext.Clear();
        }

        var envelope = publisher.Last<OutboxTestEvent>();
        Assert.NotNull(envelope);

        // 投递的是**冻结的信封**：拿投递时刻的 holder 覆盖它，会让消费端的消费锁认错身份
        Assert.Equal("holder-at-publish", envelope.Context!.HolderId);
    }
}
