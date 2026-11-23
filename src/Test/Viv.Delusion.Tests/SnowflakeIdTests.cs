using Viv.Delusion.Magic;
using Viv.Delusion.Snowflake;

namespace Viv.Delusion.Tests;

/// <summary>
/// 雪花 ID —— 单机严格递增且唯一；机器段可从 ID 还原。
/// IdMagic 走的是装配式默认实例（未装配时按兜底机器号懒创建），本类只验证主路径。
/// </summary>
public class SnowflakeIdTests
{
    [Fact]
    public void 生成器连续ID严格递增且唯一()
    {
        var gen = new SnowflakeIdGenerator(7);
        var seen = new HashSet<long>();
        long prev = 0;
        for (int i = 0; i < 5000; i++)
        {
            var id = gen.NextId();
            Assert.True(id > prev, $"第 {i} 个 ID 应严格递增");
            Assert.True(seen.Add(id), $"第 {i} 个 ID 应唯一");
            prev = id;
        }
    }

    [Fact]
    public void 机器段可还原()
    {
        const long machineId = 42;
        var gen = new SnowflakeIdGenerator(machineId);
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(machineId, (gen.NextId() >> 12) & 1023);
        }
    }

    [Fact]
    public void 不同机器ID生成不同ID()
    {
        var a = new SnowflakeIdGenerator(1).NextId();
        var b = new SnowflakeIdGenerator(2).NextId();
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void 机器ID越界抛异常()
    {
        Assert.Throws<ArgumentException>(() => new SnowflakeIdGenerator(-1));
        Assert.Throws<ArgumentException>(() => new SnowflakeIdGenerator(1024));
    }

    [Fact]
    public void IdMagic默认机器连续唯一()
    {
        var seen = new HashSet<long>();
        for (int i = 0; i < 2000; i++)
        {
            Assert.True(seen.Add(IdMagic.NextId()), $"第 {i} 个 ID 应唯一");
        }
    }
}
