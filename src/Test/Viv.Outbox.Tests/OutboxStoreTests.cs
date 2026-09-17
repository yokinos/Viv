using System.Text.Json;
using Viv.Contracts;
using Viv.Contracts.Models;
using Viv.Fakes;
using Viv.Nana;
using Viv.Outbox.Core;

namespace Viv.Outbox.Tests;

/// <summary>
/// 写路径：<see cref="OutboxStore.EnqueueAsync"/> 只落库、不发消息。
/// 「不发消息」在这里是**结构上**保证的（这个类根本拿不到发布器），测试用反射把它钉死。
/// </summary>
public class OutboxStoreTests
{
    private static (OutboxStore Store, StubOutboxRepository Repo, TestContext Context, RecordingLogger Logger) Build()
    {
        var repo = new StubOutboxRepository();
        var context = new TestContext();
        var logger = new RecordingLogger();
        return (new OutboxStore(repo, context, logger), repo, context, logger);
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
