using System.Reflection;
using Viv.Contracts.Enums;
using Viv.Contracts.Exceptions;
using Viv.Contracts.Interface;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Base;
using Viv.Momo.Interface;
using Viv.Redis;

namespace Viv.Momo.Tests;

public class DataAccessCacheBaseTests
{
    [Fact]
    public async Task GetCacheAsync_Redis故障_回源数据库不抛连接异常()
    {
        var sut = new CacheSut(
            DispatchProxy.Create<IVivContext, NopProxy>(),
            DispatchProxy.Create<IMomoDbContext, NopProxy>(),
            DispatchProxy.Create<IRedisService, ThrowingRedisProxy>(),
            new StubCacheLogger());
        sut.DbValue = new TestBucket { Name = "from-db" };

        var result = await sut.GetCacheAsync(1);

        Assert.Equal("from-db", result?.Name);
        Assert.Equal(1, sut.DbCalls);
    }

    [Fact]
    public async Task GetCacheAsync_数据库故障_不误当成缓存降级不重查()
    {
        var sut = new CacheSut(
            DispatchProxy.Create<IVivContext, NopProxy>(),
            DispatchProxy.Create<IMomoDbContext, NopProxy>(),
            DispatchProxy.Create<IRedisService, CacheMissRedisProxy>(),
            new StubCacheLogger())
        {
            DbException = new VivConnectionException(VivConnType.SqlServer, "db down")
        };

        var ex = await Assert.ThrowsAsync<VivConnectionException>(() => sut.GetCacheAsync(1));

        Assert.Equal(VivConnType.SqlServer, ex.ConnType);
        Assert.Equal(1, sut.DbCalls);
    }

    private sealed class TestBucket : ICacheBucket
    {
        public string Name { get; set; } = "";
        public TimeSpan CacheTime => TimeSpan.FromMinutes(1);
        public string GetCacheKey(params object[] keys) => "test:" + string.Join(":", keys);
    }

    private sealed class CacheSut : DataAccessCacheBase<TestBucket>
    {
        public TestBucket? DbValue { get; set; }
        public Exception? DbException { get; set; }
        public int DbCalls { get; private set; }

        public CacheSut(IVivContext context, IMomoDbContext dbContext, IRedisService redisService, ILoggerContract logger)
            : base(context, dbContext, redisService, logger)
        {
        }

        public override Task<TestBucket?> GetDbAsync(params object[] keys)
        {
            DbCalls++;
            if (DbException is not null)
                throw DbException;
            return Task.FromResult(DbValue);
        }
    }

    private class ThrowingRedisProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => throw new VivConnectionException(VivConnType.Redis, "down");
    }

    /// <summary>读 miss、取锁成功，用于走到 GetDbAsync。</summary>
    private class CacheMissRedisProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
                return null;

            var rt = targetMethod.ReturnType;
            if (rt == typeof(Task<bool>))
                return Task.FromResult(true);
            if (rt == typeof(bool))
                return true;
            if (rt.IsGenericType && rt.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var inner = rt.GetGenericArguments()[0];
                var value = inner.IsValueType ? Activator.CreateInstance(inner) : null;
                return typeof(Task).GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(inner)
                    .Invoke(null, [value]);
            }
            if (rt == typeof(Task))
                return Task.CompletedTask;
            if (rt.IsValueType)
                return Activator.CreateInstance(rt);
            return null;
        }
    }

    private class NopProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
                return null;
            if (targetMethod.ReturnType == typeof(void) || targetMethod.ReturnType == typeof(Task))
                return targetMethod.ReturnType == typeof(Task) ? Task.CompletedTask : null;
            if (targetMethod.ReturnType.IsValueType)
                return Activator.CreateInstance(targetMethod.ReturnType);
            return null;
        }
    }

    private sealed class StubCacheLogger : ILoggerContract
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
