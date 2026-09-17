using System.Text.Json;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Log;
using Viv.Nana;
using Viv.Outbox.Core;

namespace Viv.Outbox.Tests;

/// <summary>测试事件。必须继承 <see cref="NanaEvent"/> —— 发件箱投的就是跨进程那族。</summary>
public class OutboxTestEvent : NanaEvent
{
    public string Payload { get; set; } = string.Empty;

    public int Number { get; set; }
}

/// <summary>记下 Error / Warning 的内存日志桩（照其它测试套的手写桩风格，不引 Moq）。</summary>
public sealed class StubLogger : ILoggerContract
{
    public List<string> Infos { get; } = new();

    public List<string> Warnings { get; } = new();

    public List<string> Errors { get; } = new();

    public List<Exception> ErrorExceptions { get; } = new();

    public void Info(string message, params object[] args) => Infos.Add(message);

    public void Debug(string message, params object[] args) { }

    public void Warning(string message, params object[] args) => Warnings.Add(message);

    public void Fatal(string message, params object[] args) { }

    public void Fatal(string message, Exception ex, params object[] args) { }

    public void Error(string message, params object[] args) => Errors.Add(message);

    public void Error(string message, Exception ex, params object[] args)
    {
        Errors.Add(message);
        ErrorExceptions.Add(ex);
    }
}

/// <summary>捕获被投递的信封，并可预设「投递必失败」。</summary>
public sealed class StubPublisher : IVivEventPublisher
{
    public List<object> Envelopes { get; } = new();

    public Exception? PublishException { get; set; }

    /// <summary>取最后一次被投递的信封（类型的错配会在这里现形，正是要测的）。</summary>
    public NanaEnvelope<T>? Last<T>() where T : NanaEvent => Envelopes.Count == 0 ? null : Envelopes[^1] as NanaEnvelope<T>;

    public ValueTask<bool> PublishAsync<T>(T content, CancellationToken cancellationToken = default) where T : NanaEvent
        => ValueTask.FromResult(true);

    public ValueTask<bool> PublishEnvelopeAsync<T>(NanaEnvelope<T> envelope, CancellationToken cancellationToken = default) where T : NanaEvent
    {
        if (PublishException is not null) throw PublishException;
        Envelopes.Add(envelope);
        return ValueTask.FromResult(true);
    }

    public ValueTask<bool> PublishDelayAsync<T>(TimeSpan delayTTL, T content, CancellationToken cancellationToken = default) where T : NanaEvent
        => ValueTask.FromResult(true);

    public ValueTask<bool> PublishDelayAsync<T>(TimeSpan delayTTL, NanaEnvelope<T> envelope, CancellationToken cancellationToken = default) where T : NanaEvent
        => ValueTask.FromResult(true);
}

/// <summary>固定返回一份上下文的 <see cref="IVivContext"/> 桩。</summary>
public sealed class StubContext : IVivContext
{
    public VivContextContent? Snapshot { get; set; }

    public long AppId => Snapshot?.AppId ?? 0;

    public long SubjectId => Snapshot?.SubjectId ?? 0;

    public long UserId => Snapshot?.UserId ?? 0;

    public string TraceId => Snapshot?.TraceId ?? string.Empty;

    public void SetSnapshot(VivContextContent model) => Snapshot = model;

    public VivContextContent? GetRawSnapshot() => Snapshot;

    public void Clear() => Snapshot = null;
}

/// <summary>
/// 发件箱仓储桩：记录每一次状态变更，认领批次由测试脚本编排。
/// </summary>
public sealed class StubOutboxRepository : IOutboxRepository
{
    public List<OutboxMessage> Inserted { get; } = new();

    public List<(long Id, DateTime SentAt)> Sent { get; } = new();

    public List<(long Id, int RetryCount, DateTime NextRetryAt, string? LastError)> Pending { get; } = new();

    public List<(long Id, int RetryCount, string? LastError)> Failed { get; } = new();

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
}

/// <summary>按 <c>OutboxStore</c> 的写法造一段 payload，供投递侧测试用。</summary>
public static class TestPayload
{
    /// <summary>
    /// 序列化选项必须与 <c>OutboxJson.Options</c> 一致 —— 这里刻意**另起一份**而不是抄它的实例：
    /// 两边各写各的，一旦哪天 Outbox 把 camelCase 去掉，这里就会解析不出来、测试立刻红。
    /// 共用同一个实例反而会把「选项漂移」这个要防的东西一起防掉。
    /// </summary>
    internal static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    public static string For<T>(T content, long messageId, VivContextContent? context = null) where T : NanaEvent
    {
        var envelope = new NanaEnvelope<T> { Content = content, MessageId = messageId, Context = context };
        return JsonSerializer.Serialize(envelope, JsonOptions);
    }
}
