using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Viv.Fakes;
using Viv.Ouroboros.Core.Service;
using Viv.Redis;

namespace Viv.Ouroboros.Tests;

/// <summary>
/// 版本闸门的三个时间窗口：共享戳节流 5 秒、指纹节流 3 秒、失败退避 60 秒。
/// 全部靠 <see cref="FakeTimeProvider"/> 拨时间验证 —— 真等一遍要一分多钟。
///
/// 指纹那条路径只验「读不到库时不算失败」：正向比对要真数据库，属于集成测试的范围。
/// </summary>
public class ConfigVersionGateTests
{
    /// <summary>建一个闸门 + Redis 探针 + 可控时钟；<paramref name="withRedis"/> 为 false 表示这套部署没启用 Redis。</summary>
    private static (ConfigVersionGate Gate, TestProxy Redis, FakeTimeProvider Clock) Create(
        bool withRedis = true, bool redisThrows = false, string stamp = "stamp-1")
    {
        var clock = new FakeTimeProvider();

        // 探针要拿回来数调用次数，动态类型没法在构造后取，只能在 configure 里捕获
        TestProxy? proxy = null;
        var redis = TestProxy.Create<IRedisService>(p =>
        {
            proxy = p;
            p.Returns[typeof(string)] = stamp;

            if (redisThrows)
            {
                p.ThrowOnAnyCall = true;
                p.ThrowException = new InvalidOperationException("redis down");
            }
        });

        var services = new ServiceCollection();
        if (withRedis) services.AddSingleton(redis);

        // 同一个 provider 兼作 scope factory：里面没有 IMomoDbContext，
        // 指纹路径会拿到 null 并原样返回，于是本组用例只测得到共享戳那条
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        return (new ConfigVersionGate(provider, scopeFactory, new RecordingLogger(), clock), proxy!, clock);
    }

    /// <summary>只数共享戳的读取，Add 之类的不算。</summary>
    private static int GetCalls(TestProxy redis) => redis.Calls.Count(x => x == "Get");

    [Fact]
    public void 首次读戳会自增一次版本()
    {
        var (gate, redis, _) = Create();

        Assert.Equal(0, gate.Generation);

        // 首次读到的戳与「没见过」（null）不同，于是算一次变更、自增到 1
        Assert.Equal(1, gate.EnsureFresh());
        Assert.Equal(1, GetCalls(redis));
    }

    [Fact]
    public void 节流窗口内不重复读共享戳()
    {
        var (gate, redis, clock) = Create();

        gate.EnsureFresh();
        Assert.Equal(1, GetCalls(redis));

        clock.Advance(TimeSpan.FromSeconds(4));   // 未到 5 秒
        gate.EnsureFresh();
        Assert.Equal(1, GetCalls(redis));

        clock.Advance(TimeSpan.FromSeconds(2));   // 累计 6 秒
        gate.EnsureFresh();
        Assert.Equal(2, GetCalls(redis));
    }

    [Fact]
    public void 戳变了才自增_没变不动()
    {
        var (gate, redis, clock) = Create();

        Assert.Equal(1, gate.EnsureFresh());      // 首读基线

        clock.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(1, gate.EnsureFresh());      // 戳没变，版本不动

        redis.Returns[typeof(string)] = "stamp-2";
        clock.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(2, gate.EnsureFresh());      // 别处改过配置
    }

    [Fact]
    public void Redis读失败后退避60秒再重试()
    {
        var (gate, redis, clock) = Create(redisThrows: true);

        gate.EnsureFresh();                       // 第一次就抛，进入退避
        Assert.Equal(1, GetCalls(redis));
        Assert.Equal(0, gate.Generation);         // 读不到戳 ≠ 配置变过

        clock.Advance(TimeSpan.FromSeconds(59));
        gate.EnsureFresh();                       // 还在退避窗口内
        Assert.Equal(1, GetCalls(redis));

        clock.Advance(TimeSpan.FromSeconds(2));   // 累计 61 秒，窗口已过
        gate.EnsureFresh();
        Assert.Equal(2, GetCalls(redis));
    }

    [Fact]
    public void 没有Redis时只增本进程版本()
    {
        var (gate, redis, _) = Create(withRedis: false);

        Assert.Equal(0, gate.EnsureFresh());      // 不抛异常，也不去碰并不存在的 Redis
        Assert.Empty(redis.Calls);

        Assert.Equal(1, gate.Publish());
        Assert.Equal(2, gate.Publish());
        Assert.Equal(2, gate.Generation);
    }

    [Fact]
    public void Publish写戳且本进程立刻生效()
    {
        var (gate, redis, _) = Create();

        Assert.Equal(1, gate.Publish());
        Assert.Contains("Add", redis.Calls);
        Assert.Equal(1, gate.Generation);
    }
}
