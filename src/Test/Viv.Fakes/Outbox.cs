using Viv.Nana;
using Viv.Outbox;
using Viv.Outbox.Core;

namespace Viv.Fakes;

/// <summary>
/// 发件箱仓储替身：记录每一次状态变更，认领批次由测试脚本编排。
///
/// 只能声明成 internal：<see cref="IOutboxRepository"/> 本身是 internal 的，
/// public 类实现 internal 接口是 CS0061。故本程序集对 <c>Viv.Outbox.Tests</c> 开了 IVT。
///
/// <see cref="ClaimScript"/> 用队列逐次吐出批次、用完后回空 —— 投递器是「一直认领到认不出为止」的
/// 排空循环，脚本化既能模拟积压，也能模拟「认空了」这个终止条件。
/// </summary>
internal class StubOutboxRepository : IOutboxRepository
{
    public List<OutboxMessage> Inserted { get; } = [];

    public List<(long Id, DateTime SentAt)> Sent { get; } = [];

    public List<(long Id, int RetryCount, DateTime NextRetryAt, string? LastError)> Pending { get; } = [];

    public List<(long Id, int RetryCount, string? LastError)> Failed { get; } = [];

    public int EnsureTableCalls { get; private set; }

    public int ReleaseExpiredCalls { get; private set; }

    public int ClaimCalls { get; private set; }

    public int CleanupCalls { get; private set; }

    public DateTime? LastClaimNow { get; private set; }

    public DateTime? LastClaimLeaseUntil { get; private set; }

    public int LastClaimBatchSize { get; private set; }

    /// <summary>逐次返回的认领批次；用完后返回空（测试显式写出「认空了」）。</summary>
    public Queue<List<OutboxMessage>> ClaimScript { get; } = new();

    public Exception? ClaimException { get; set; }

    public Exception? EnsureTableException { get; set; }

    /// <summary>前几次建表抛异常，用完后恢复成功。给调度层的重试测。</summary>
    public int EnsureTableFailTimes { get; set; }

    /// <summary>false = 状态回写影响 0 行，模拟租约已经不属于这次认领。</summary>
    public bool OwnsLease { get; set; } = true;

    public int MarkSentCalls { get; private set; }

    public DateTime? LastMarkLeaseUntil { get; private set; }

    public bool InsertResult { get; set; } = true;

    /// <summary>清理是否「删掉了东西」。true 会让 worker 一直删到上限，测试要的就是这个边界。</summary>
    public bool CleanupResult { get; set; }

    public Task EnsureTableAsync(CancellationToken cancellationToken = default)
    {
        EnsureTableCalls++;
        if (EnsureTableException is not null) throw EnsureTableException;
        if (EnsureTableFailTimes > 0)
        {
            EnsureTableFailTimes--;
            throw new InvalidOperationException("表建不出来");
        }

        return Task.CompletedTask;
    }

    public Task<bool> InsertAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        Inserted.Add(message);
        return Task.FromResult(InsertResult);
    }

    public Task ReleaseExpiredLeasesAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        ReleaseExpiredCalls++;
        return Task.CompletedTask;
    }

    public Task<List<OutboxMessage>> ClaimBatchAsync(
        int batchSize, DateTime now, DateTime leaseUntil, CancellationToken cancellationToken = default)
    {
        ClaimCalls++;
        LastClaimNow = now;
        LastClaimLeaseUntil = leaseUntil;
        LastClaimBatchSize = batchSize;

        if (ClaimException is not null) throw ClaimException;

        return Task.FromResult(ClaimScript.Count > 0 ? ClaimScript.Dequeue() : new List<OutboxMessage>());
    }

    public Task<bool> MarkSentAsync(long id, DateTime now, DateTime leaseUntil, CancellationToken cancellationToken = default)
    {
        MarkSentCalls++;
        LastMarkLeaseUntil = leaseUntil;
        if (!OwnsLease) return Task.FromResult(false);

        Sent.Add((id, now));
        return Task.FromResult(true);
    }

    public Task<bool> MarkPendingAsync(
        long id, int retryCount, DateTime nextRetryAt, DateTime leaseUntil, string? lastError, CancellationToken cancellationToken = default)
    {
        LastMarkLeaseUntil = leaseUntil;
        if (!OwnsLease) return Task.FromResult(false);

        Pending.Add((id, retryCount, nextRetryAt, lastError));
        return Task.FromResult(true);
    }

    public Task<bool> MarkFailedAsync(
        long id, int retryCount, DateTime leaseUntil, string? lastError, CancellationToken cancellationToken = default)
    {
        LastMarkLeaseUntil = leaseUntil;
        if (!OwnsLease) return Task.FromResult(false);

        Failed.Add((id, retryCount, lastError));
        return Task.FromResult(true);
    }

    public Task<bool> CleanupBatchAsync(DateTime cutoff, int batchSize, CancellationToken cancellationToken = default)
    {
        CleanupCalls++;
        return Task.FromResult(CleanupResult);
    }

    public (long Pending, long Failed) Depth { get; set; }

    public Task<(long Pending, long Failed)> CountDepthAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Depth);
}

/// <summary>
/// 发件箱入队替身 —— ChatService 采用样本用它断言入队而不是当场 Publish。
/// </summary>
public sealed class RecordingOutbox : IVivOutbox
{
    public List<NanaEvent> Enqueued { get; } = [];

    public bool Result { get; set; } = true;

    public Task<bool> EnqueueAsync<T>(T content, CancellationToken cancellationToken = default) where T : NanaEvent
    {
        if (content is null) return Task.FromResult(false);
        Enqueued.Add(content);
        return Task.FromResult(Result);
    }
}

/// <summary>
/// Inbox 仓储替身。与 StubOutboxRepository 同因：IInboxRepository 是 internal。
/// </summary>
internal sealed class StubInboxRepository : IInboxRepository
{
    /// <summary>落库的键 —— 是 InboxStore 拼好前缀之后的那一串（<c>msg:42</c> / <c>biz:order:42</c>），
    /// 所以「消息 42 与业务键 42 不互撞」这类断言在这个集合上才成立。</summary>
    public HashSet<(string Service, string Key)> Accepted { get; } = [];

    public int EnsureTableCalls { get; private set; }

    public int CleanupCalls { get; private set; }

    public DateTime? LastCleanupCutoff { get; private set; }

    public int LastCleanupBatchSize { get; private set; }

    public bool DuplicateReturnsFalse { get; set; } = true;

    /// <summary>清理是否「删掉了东西」。true 会让清理器一直删到上限，测试要的就是这个边界。</summary>
    public bool CleanupResult { get; set; }

    /// <summary>逐次返回的清理结果；用完后回落到 <see cref="CleanupResult"/>。</summary>
    public Queue<bool> CleanupScript { get; } = new();

    public Task EnsureTableAsync(CancellationToken cancellationToken = default)
    {
        EnsureTableCalls++;
        return Task.CompletedTask;
    }

    public Task<bool> CleanupBatchAsync(DateTime cutoff, int batchSize, CancellationToken cancellationToken = default)
    {
        CleanupCalls++;
        LastCleanupCutoff = cutoff;
        LastCleanupBatchSize = batchSize;
        return Task.FromResult(CleanupScript.Count > 0 ? CleanupScript.Dequeue() : CleanupResult);
    }

    public Task<bool> TryInsertAsync(string serviceName, string idempotentKey, DateTime acceptedAt, CancellationToken cancellationToken = default)
    {
        var key = (serviceName, idempotentKey);
        if (DuplicateReturnsFalse && !Accepted.Add(key))
            return Task.FromResult(false);
        Accepted.Add(key);
        return Task.FromResult(true);
    }
}

