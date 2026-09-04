using System.Reflection;
using Viv.Contracts.Enums;
using Viv.Contracts.Exceptions;
using Viv.Log;
using Viv.Redis;

namespace Viv.Engine.Tests;

public class DistributedLockAccessorTests
{
    [Fact]
    public async Task AcquireLockAsync_Redis连接失败_包装DistributedLockException()
    {
        var accessor = new DistributedLockAccessor(ThrowingRedis(), new StubLockLogger());

        var ex = await Assert.ThrowsAsync<DistributedLockException>(
            () => accessor.AcquireLockAsync("lock:k", TimeSpan.FromSeconds(1)));

        var inner = Assert.IsType<VivConnectionException>(ex.InnerException);
        Assert.Equal(VivConnType.Redis, inner.ConnType);
    }

    [Fact]
    public async Task ReleaseLockAsync_Redis连接失败_包装DistributedLockException()
    {
        var accessor = new DistributedLockAccessor(ThrowingRedis(), new StubLockLogger());

        var ex = await Assert.ThrowsAsync<DistributedLockException>(
            () => accessor.ReleaseLockAsync("lock:k"));

        Assert.IsType<VivConnectionException>(ex.InnerException);
    }

    [Fact]
    public async Task AcquireLockWithRetryAsync_耗尽后包装连接异常()
    {
        var accessor = new DistributedLockAccessor(ThrowingRedis(), new StubLockLogger());

        var ex = await Assert.ThrowsAsync<DistributedLockException>(
            () => accessor.AcquireLockWithRetryAsync("lock:k", TimeSpan.FromSeconds(1), maxRetryCount: 2, baseDelay: 1, maxDelay: 1));

        Assert.IsType<VivConnectionException>(ex.InnerException);
        Assert.Equal(2, ex.RetryCount);
    }

    private static IRedisService ThrowingRedis()
        => DispatchProxy.Create<IRedisService, ThrowingRedisProxy>();

    private class ThrowingRedisProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => throw new VivConnectionException(VivConnType.Redis, "redis down");
    }

    private sealed class StubLockLogger : ILoggerContract
    {
        public void Info(string message, params object[] args) { }
        public void Debug(string message, params object[] args) { }
        public void Warning(string message, params object[] args) { }
        public void Error(string message, params object[] args) { }
        public void Error(string message, Exception ex, params object[] args) { }
        public void Fatal(string message, params object[] args) { }
        public void Fatal(string message, Exception ex, params object[] args) { }
    }
}
