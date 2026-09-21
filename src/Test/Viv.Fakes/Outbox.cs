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

    public bool InsertResult { get; set; } = true;

    /// <summary>清理是否「删掉了东西」。true 会让 worker 一直删到上限，测试要的就是这个边界。</summary>
    public bool CleanupResult { get; set; }

    public Task EnsureTableAsync(CancellationToken cancellationToken = default)
    {
        EnsureTableCalls++;
        if (EnsureTableException is not null) throw EnsureTableException;
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

    public Task MarkSentAsync(long id, DateTime now, CancellationToken cancellationToken = default)
    {
        Sent.Add((id, now));
        return Task.CompletedTask;
    }

    public Task MarkPendingAsync(
        long id, int retryCount, DateTime nextRetryAt, string? lastError, CancellationToken cancellationToken = default)
    {
        Pending.Add((id, retryCount, nextRetryAt, lastError));
        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(long id, int retryCount, string? lastError, CancellationToken cancellationToken = default)
    {
        Failed.Add((id, retryCount, lastError));
        return Task.CompletedTask;
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
    public HashSet<(string Service, long MessageId)> Accepted { get; } = [];

    public int EnsureTableCalls { get; private set; }

    public bool DuplicateReturnsFalse { get; set; } = true;

    public Task EnsureTableAsync(CancellationToken cancellationToken = default)
    {
        EnsureTableCalls++;
        return Task.CompletedTask;
    }

    public Task<bool> TryInsertAsync(string serviceName, long messageId, DateTime acceptedAt, CancellationToken cancellationToken = default)
    {
        var key = (serviceName, messageId);
        if (DuplicateReturnsFalse && !Accepted.Add(key))
            return Task.FromResult(false);
        Accepted.Add(key);
        return Task.FromResult(true);
    }
}

