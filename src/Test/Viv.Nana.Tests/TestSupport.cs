using Viv.Contracts.Exceptions;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Log;
using Viv.Nana.Core;

namespace Viv.Nana.Tests
{
    /// <summary>测试用消息体（事件后缀）</summary>
    public class TestApexEvent : NanaEvent
    {
        public string Payload { get; set; } = string.Empty;
    }

    /// <summary>小写 event 后缀，验证命名约定大小写不敏感</summary>
    public class LowerEvent : NanaEvent { }

    /// <summary>非 NanaEvent 的普通类，验证命名约定不要求 Event 后缀</summary>
    public class PlainMessage { }

    /// <summary>记录 Error 调用的内存日志桩</summary>
    public class StubLogger : ILoggerContract
    {
        public List<string> Errors { get; } = new();
        public List<(string Message, Exception? Ex)> ErrorWithException { get; } = new();
        public List<string> Warnings { get; } = new();

        public void Info(string message, params object[] args) { }
        public void Debug(string message, params object[] args) { }
        public void Warning(string message, params object[] args) => Warnings.Add(message);
        public void Fatal(string message, params object[] args) { }
        public void Fatal(string message, Exception ex, params object[] args) { }
        public void Error(string message, params object[] args) => Errors.Add(message);
        public void Error(string message, Exception ex, params object[] args) => ErrorWithException.Add((message, ex));
    }

    public class FakeContext : IVivContext
    {
        public long AppId => throw new NotImplementedException();

        public long SubjectId => throw new NotImplementedException();

        public long UserId => throw new NotImplementedException();

        public string TraceId => throw new NotImplementedException();

        public void Clear()
        {

        }

        public VivContextContent? GetRawSnapshot()
        {
            return new VivContextContent();
        }

        public void SetSnapshot(VivContextContent model)
        {

        }
    }

    /// <summary>记录信封延迟投递调用的发布器桩（延迟重投测试用）</summary>
    public class StubPublisher : IVivEventPublisher
    {
        public bool PublishDelayCalled { get; private set; }
        public object? LastEnvelope { get; private set; }
        public bool Result { get; set; } = true;

        public Task<bool> PublishAsync<T>(T content, CancellationToken cancellationToken = default) where T : NanaEvent
            => Task.FromResult(Result);

        public Task<bool> PublishDelayAsync<T>(TimeSpan delayTTL, T content, CancellationToken cancellationToken = default) where T : NanaEvent
            => Task.FromResult(Result);

        public Task<bool> PublishDelayAsync<T>(TimeSpan delayTTL, NanaEnvelope<T> envelope, CancellationToken cancellationToken = default) where T : NanaEvent
        {
            PublishDelayCalled = true;
            LastEnvelope = envelope;
            return Task.FromResult(Result);
        }
    }

    /// <summary>记录取锁/释放调用的分布式锁桩</summary>
    public class StubDistributedLock : IDistributedLock
    {
        public bool AcquireResult { get; set; } = true;
        public bool IsHeldResult { get; set; } = true;
        public bool IsHeldThrows { get; set; }
        public int AcquireCalls { get; private set; }
        public int HeldCalls { get; private set; }
        public int ReleaseCalls { get; private set; }
        public string? LastLockKey { get; private set; }

        public Task<bool> AcquireLockAsync(string lockKey, TimeSpan expire, string? lockHolderId = null, bool isReentrant = true)
        {
            AcquireCalls++;
            LastLockKey = lockKey;
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
}
