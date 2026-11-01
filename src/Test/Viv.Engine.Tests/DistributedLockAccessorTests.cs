using Viv.Contracts.Enums;
using Viv.Contracts.Exceptions;
using Viv.Engine.Locking;
using Viv.Fakes;
using Viv.Redis;

namespace Viv.Engine.Tests;

public class DistributedLockAccessorTests
{
    [Fact]
    public async Task AcquireLockAsync_Redis连接失败_包装DistributedLockException()
    {
        var accessor = new DistributedLockAccessor(ThrowingRedis(), new RecordingLogger());

        var ex = await Assert.ThrowsAsync<DistributedLockException>(
            () => accessor.AcquireLockAsync("lock:k", TimeSpan.FromSeconds(1)));

        var inner = Assert.IsType<VivConnectionException>(ex.InnerException);
        Assert.Equal(VivConnType.Redis, inner.ConnType);
    }

    [Fact]
    public async Task ReleaseLockAsync_Redis连接失败_包装DistributedLockException()
    {
        var accessor = new DistributedLockAccessor(ThrowingRedis(), new RecordingLogger());

        var ex = await Assert.ThrowsAsync<DistributedLockException>(
            () => accessor.ReleaseLockAsync("lock:k"));

        Assert.IsType<VivConnectionException>(ex.InnerException);
    }

    [Fact]
    public async Task AcquireLockWithRetryAsync_耗尽后包装连接异常()
    {
        var accessor = new DistributedLockAccessor(ThrowingRedis(), new RecordingLogger());

        var ex = await Assert.ThrowsAsync<DistributedLockException>(
            () => accessor.AcquireLockWithRetryAsync("lock:k", TimeSpan.FromSeconds(1), maxRetryCount: 2, baseDelay: 1, maxDelay: 1));

        Assert.IsType<VivConnectionException>(ex.InnerException);
        Assert.Equal(2, ex.RetryCount);
    }

    [Fact]
    public async Task AcquireLockWithRetryAsync_RetryCount为零_仍尝试一次()
    {
        // maxRetryCount 是「重试次数」：0 = 只尝试一次，不是一次都不试
        var accessor = new DistributedLockAccessor(ThrowingRedis(), new RecordingLogger());

        var ex = await Assert.ThrowsAsync<DistributedLockException>(
            () => accessor.AcquireLockWithRetryAsync("lock:k", TimeSpan.FromSeconds(1), maxRetryCount: 0));

        Assert.Equal(0, ex.RetryCount);
        Assert.IsType<VivConnectionException>(ex.InnerException);
    }

    private static IRedisService ThrowingRedis()
        => TestProxy.Create<IRedisService>(p =>
        {
            p.ThrowOnAnyCall = true;
            p.ThrowException = new VivConnectionException(VivConnType.Redis, "redis down");
        });
}
