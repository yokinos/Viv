using Viv.Contracts.Exceptions;
using Viv.Contracts.Interface;
using Viv.Nana;
using Viv.Nana.Core;

namespace Viv.Fakes;

/// <summary>
/// <see cref="IVivEventPublisher"/> 记录替身 —— 原本 Nana / Outbox / Elysia / Herta 各有一个副本。
///
/// 两个列表分开记：<see cref="Published"/>（内容版）与 <see cref="Envelopes"/>（信封版）。
/// Outbox 断言「库里的消息投出去了」用的是 <c>Envelopes</c>，若内容版也往里塞，
/// 那种 <c>Assert.Single(...)</c> 会莫名其妙变成 2 条。
///
/// 异常旋钮同样分两个：<see cref="PublishException"/> 只在信封版 <c>PublishEnvelopeAsync</c>
/// 上抛（Outbox 用它验「投递失败退回待发」），<see cref="DelayException"/> 只在信封版延迟投递上抛。
/// 扩大到所有重载，走内容版的用例会一起炸。
/// </summary>
public class RecordingEventPublisher : IVivEventPublisher
{
    /// <summary>内容版 <c>PublishAsync</c> 的投递记录</summary>
    public List<NanaEvent> Published { get; } = [];

    /// <summary>信封版投递记录（<c>PublishEnvelopeAsync</c> 与信封版延迟投递）</summary>
    public List<object> Envelopes { get; } = [];

    /// <summary>最后一次投出去的信封</summary>
    public object? LastEnvelope { get; private set; }

    /// <summary>
    /// 信封版延迟投递是否被调用过。只信封版置位 —— 「重投走的是哪条路」正是延迟重投那几条
    /// 测试要钉的东西（<c>VivConsumer.RedeliverAsync</c> 走信封重载，所以那边断言的是 False）。
    /// </summary>
    public bool PublishDelayEnvelopeCalled { get; private set; }

    /// <summary>所有重载的返回值</summary>
    public bool Result { get; set; } = true;

    /// <summary>信封版 <c>PublishEnvelopeAsync</c> 抛这个异常</summary>
    public Exception? PublishException { get; set; }

    /// <summary>信封版延迟投递抛这个异常</summary>
    public Exception? DelayException { get; set; }

    /// <summary>取最后一次被投递的信封 —— 类型错配会在这里现形（那正是要测的）</summary>
    public NanaEnvelope<T>? Last<T>() where T : NanaEvent
        => Envelopes.Count == 0 ? null : Envelopes[^1] as NanaEnvelope<T>;

    public ValueTask<bool> PublishAsync<T>(T content, CancellationToken cancellationToken = default) where T : NanaEvent
    {
        Published.Add(content);
        return ValueTask.FromResult(Result);
    }

    public ValueTask<bool> PublishEnvelopeAsync<T>(NanaEnvelope<T> envelope, CancellationToken cancellationToken = default) where T : NanaEvent
    {
        if (PublishException is not null)
            throw PublishException;

        Record(envelope);
        return ValueTask.FromResult(Result);
    }

    public ValueTask<bool> PublishDelayAsync<T>(TimeSpan delayTTL, T content, CancellationToken cancellationToken = default) where T : NanaEvent
        => ValueTask.FromResult(Result);

    public ValueTask<bool> PublishDelayAsync<T>(TimeSpan delayTTL, NanaEnvelope<T> envelope, CancellationToken cancellationToken = default) where T : NanaEvent
    {
        if (DelayException is not null)
            throw DelayException;

        PublishDelayEnvelopeCalled = true;
        Record(envelope);
        return ValueTask.FromResult(Result);
    }

    private void Record(object envelope)
    {
        Envelopes.Add(envelope);
        LastEnvelope = envelope;
    }
}

/// <summary>
/// <see cref="IVivLocalEventPublisher"/> 记录替身（进程内本地队列那族）。
///
/// 必须与 <see cref="RecordingEventPublisher"/> 分成两个类、不能合并：两个接口都有
/// <c>ValueTask&lt;bool&gt; PublishAsync&lt;T&gt;(T, CancellationToken)</c>，只有泛型约束不同
/// （<c>where T : NanaEvent</c> vs <c>where T : NanaLocalEvent</c>），而约束不参与签名 ——
/// 同一个类隐式实现两个接口必然 CS0111。
/// </summary>
public class RecordingLocalEventPublisher : IVivLocalEventPublisher
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

/// <summary>
/// <see cref="IDistributedLock"/> 替身 —— 记录取锁/释放调用，取锁结果由测试摆布。
///
/// <see cref="AcquireLockWithExecuteAsync{T}"/> 是真实实现不是空桩（取锁 → 执行 →
/// finally 释放，拿不到锁走 fallback 或抛）：它是行为，简化掉就测不出「拿不到锁时到底走没走业务」。
/// </summary>
public class RecordingDistributedLock : IDistributedLock
{
    public bool AcquireResult { get; set; } = true;

    public bool IsHeldResult { get; set; } = true;

    public bool IsHeldThrows { get; set; }

    public int AcquireCalls { get; private set; }

    public int HeldCalls { get; private set; }

    public int ReleaseCalls { get; private set; }

    public string? LastLockKey { get; private set; }

    public string? LastHolderId { get; private set; }

    public Exception? AcquireException { get; set; }

    public Task<bool> AcquireLockAsync(string lockKey, TimeSpan expire, string? lockHolderId = null, bool isReentrant = true)
    {
        AcquireCalls++;
        LastLockKey = lockKey;
        LastHolderId = lockHolderId;
        if (AcquireException is not null)
            throw AcquireException;
        return Task.FromResult(AcquireResult);
    }

    public Task<bool> IsLockHeldAsync(string lockKey)
    {
        HeldCalls++;
        if (IsHeldThrows)
            throw new DistributedLockException(lockKey, 0);
        return Task.FromResult(IsHeldResult);
    }

    public Task<bool> ReleaseLockAsync(string lockKey, string? lockHolderId = null, bool isReentrant = true)
    {
        ReleaseCalls++;
        return Task.FromResult(true);
    }

    public Task<bool> AcquireLockWithRetryAsync(
        string lockKey,
        TimeSpan expire,
        string? lockHolderId = null,
        bool isReentrant = true,
        int maxRetryCount = 5,
        int baseDelay = 200,
        int maxDelay = 5000,
        CancellationToken cancellationToken = default)
        => AcquireLockAsync(lockKey, expire, lockHolderId, isReentrant);

    public async Task<T> AcquireLockWithExecuteAsync<T>(
        object key,
        TimeSpan expire,
        Func<Task<T>> executeMethod,
        Func<Task<T>>? fallbackMethod = null,
        string? lockHolderId = null,
        bool isReentrant = true,
        int maxRetryCount = 5,
        int baseDelay = 200,
        int maxDelay = 5000,
        CancellationToken cancellationToken = default)
    {
        var lockKey = key?.ToString() ?? "null";
        if (!await AcquireLockAsync(lockKey, expire, lockHolderId, isReentrant))
        {
            if (fallbackMethod is not null)
                return await fallbackMethod();
            throw new DistributedLockException(lockKey, maxRetryCount);
        }

        try
        {
            return await executeMethod();
        }
        finally
        {
            await ReleaseLockAsync(lockKey, lockHolderId, isReentrant);
        }
    }
}
