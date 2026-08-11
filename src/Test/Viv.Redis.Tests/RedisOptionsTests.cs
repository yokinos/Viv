using Viv.Redis.DbAllocator;

namespace Viv.Redis.Tests;

/// <summary>
/// RedisOptions 默认值 —— 配置模型的静态锚点，改了默认值会改变所有服务的 Redis 行为。
/// </summary>
public class RedisOptionsTests
{
    [Fact]
    public void 默认配置()
    {
        var o = new RedisOptions();
        Assert.Equal(RedisMode.Standalone, o.RedisMode);
        Assert.Equal(string.Empty, o.ConnectionString);
        Assert.Empty(o.SentinelEndPoints);
        Assert.Equal(string.Empty, o.SentinelMasterName);
        Assert.Equal(5000, o.ConnectTimeout);
        Assert.Equal(5000, o.SyncTimeout);
        Assert.False(o.AllowAdmin);
        Assert.False(o.AbortOnConnectFail);
        Assert.Equal(string.Empty, o.Password);
        Assert.Equal(0, o.DefaultDatabase);
        Assert.Equal(60, o.KeepAlive);
        Assert.Equal(12, o.MaxDbIndex);
        Assert.Equal(DbSelectorType.None, o.SelectorType);
    }
}
