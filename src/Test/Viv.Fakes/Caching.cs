using Viv.Contracts.Enums;
using Viv.Contracts.Exceptions;
using Viv.Contracts.Interface;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Base;
using Viv.Momo.Interface;
using Viv.Redis;

namespace Viv.Fakes;

/// <summary>
/// 缓存链路的三件套：缓存桶、可 new 的 SUT 探针、两个 Redis 替身。
/// 三件绑在一起搬 —— <see cref="CacheSut"/> 的泛型实参就是 <see cref="TestBucket"/>，拆不开。
/// </summary>
public class TestBucket : ICacheBucket
{
    public string Name { get; set; } = "";

    public TimeSpan CacheTime => TimeSpan.FromMinutes(1);

    public string GetCacheKey(params object[] keys) => "test:" + string.Join(":", keys);
}

/// <summary>
/// 把 <see cref="DataAccessCacheBase{T}"/> 从 abstract 变成可 new 的探针 ——
/// 库读什么、抛不抛，全由测试摆布，不需要真数据库。
/// </summary>
public class CacheSut : DataAccessCacheBase<TestBucket>
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

/// <summary>Redis 替身的命名工厂 —— 调用点只表达「要哪种 Redis」，配置细节留在替身程序集里。</summary>
public static class CacheDoubles
{
    /// <summary>Redis 全挂：任何一次调用都抛连接异常 —— 验「缓存故障当 miss，回源数据库」</summary>
    public static IRedisService ThrowingRedis()
        => TestProxy.Create<IRedisService>(p =>
        {
            p.ThrowOnAnyCall = true;
            p.ThrowException = new VivConnectionException(VivConnType.Redis, "down");
        });

    /// <summary>读 miss、取锁成功 —— 用来走到子类的 GetDbAsync</summary>
    public static IRedisService CacheMissRedis()
        => TestProxy.Create<IRedisService>(p =>
        {
            // 默认回值是 default(bool) = false，那是「没抢到锁」，走不到查库那条路
            p.Returns[typeof(bool)] = true;
            p.Returns[typeof(Task<bool>)] = Task.FromResult(true);
        });
}
