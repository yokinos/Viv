using Viv.Delusion;
using Viv.Redis;
using Viv.Redis.DbAllocator;

namespace Viv.Redis.Tests;

/// <summary>
/// Db 分配器 —— 决定 Redis key 落在哪个库，是租户/key 隔离的纯逻辑。
/// KeyHashAllocator 按 key 的 CRC64 哈希分库（默认分库方式）；NoneAllocator 固定落 DefaultDatabase。
/// </summary>
public class KeyHashAllocatorTests
{
    [Fact]
    public void 同Key同库_结果确定()
    {
        var allocator = new KeyHashAllocator();
        Assert.Equal(
            allocator.AllocateDbIndex("user:100", 12),
            allocator.AllocateDbIndex("user:100", 12));
    }

    [Fact]
    public void 索引始终在范围内()
    {
        var allocator = new KeyHashAllocator();
        for (int i = 0; i < 200; i++)
        {
            int idx = allocator.AllocateDbIndex($"key:{i}", 12);
            Assert.InRange(idx, 0, 12);
        }
    }

    [Fact]
    public void maxDbIndex为null_回退0号库()
    {
        Assert.Equal(0, new KeyHashAllocator().AllocateDbIndex("key", null));
    }

    [Fact]
    public void maxDbIndex为0_恒为0号库()
    {
        var allocator = new KeyHashAllocator();
        Assert.Equal(0, allocator.AllocateDbIndex("key", 0));
        Assert.Equal(0, allocator.AllocateDbIndex("user:1", 0));
    }

    [Fact]
    public void 空白键抛异常()
    {
        var allocator = new KeyHashAllocator();
        Assert.Throws<ArgumentException>(() => allocator.AllocateDbIndex("", 12));
        Assert.Throws<ArgumentException>(() => allocator.AllocateDbIndex("   ", 12));
        Assert.Throws<ArgumentNullException>(() => allocator.AllocateDbIndex(null!, 12));
    }

    [Fact]
    public void 分组_跳过空白键()
    {
        var allocator = new KeyHashAllocator();
        var groups = allocator.AllocateGroupDbIndex(["a", "b", " "], 12);

        int totalKeys = groups.Values.Sum(x => x.Length);
        Assert.Equal(2, totalKeys);

        foreach (var key in new[] { "a", "b" })
        {
            int expected = allocator.AllocateDbIndex(key, 12);
            Assert.Contains(groups[expected], x => x.ToString() == key);
        }
    }

    [Fact]
    public void 分组_maxDb为0全落同一组()
    {
        var groups = new KeyHashAllocator().AllocateGroupDbIndex(["a", "b", "c"], 0);

        Assert.Single(groups);
        Assert.Equal(3, groups.Values.Single().Length);
    }
}

public class NoneAllocatorTests
{
    [Fact]
    public void 无配置_回退0号库()
    {
        VivConfigRegistry.Remove<RedisOptions>();
        Assert.Equal(0, new NoneAllocator().AllocateDbIndex("key", 12));
    }

    [Fact]
    public void 有配置_用DefaultDatabase()
    {
        try
        {
            VivConfigRegistry.Add(new RedisOptions { DefaultDatabase = 5 });
            Assert.Equal(5, new NoneAllocator().AllocateDbIndex("key", 12));
        }
        finally
        {
            VivConfigRegistry.Remove<RedisOptions>();
        }
    }

    [Fact]
    public void 分组_全落DefaultDatabase()
    {
        try
        {
            VivConfigRegistry.Add(new RedisOptions { DefaultDatabase = 3 });
            var groups = new NoneAllocator().AllocateGroupDbIndex(["a", "b"], 12);

            Assert.Single(groups);
            Assert.True(groups.ContainsKey(3));
            Assert.Equal(2, groups[3].Length);
        }
        finally
        {
            VivConfigRegistry.Remove<RedisOptions>();
        }
    }
}
