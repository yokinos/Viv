using Viv.Contracts;

namespace Viv.Redis.Tests;

/// <summary>
/// 分布式锁持有者上下文 —— AsyncLocal 承载，同一异步流内复用、可显式设置/重置/清除。
/// 锁的持有者 ID 是"谁持有这把锁"的身份，被 RedisService 的 Lua 脚本拿去比对防误删。
/// </summary>
public class LockHolderContextTests
{
    [Fact]
    public void 首次访问_自动生成非空Id()
    {
        var id = LockHolderContext.CurrentHolderId;
        Assert.False(string.IsNullOrWhiteSpace(id));
    }

    [Fact]
    public void 同一异步流_复用同一Id()
    {
        var first = LockHolderContext.CurrentHolderId;
        var second = LockHolderContext.CurrentHolderId;
        Assert.Equal(first, second);
    }

    [Fact]
    public void 显式设置_覆盖Id()
    {
        LockHolderContext.SetHolderId("test-holder");
        Assert.Equal("test-holder", LockHolderContext.CurrentHolderId);
    }

    [Fact]
    public void 重置_生成新Id()
    {
        var before = LockHolderContext.CurrentHolderId;
        LockHolderContext.ResetHolderId();
        var after = LockHolderContext.CurrentHolderId;
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void 清除_再访问重新生成()
    {
        var before = LockHolderContext.CurrentHolderId;
        LockHolderContext.Clear();
        var after = LockHolderContext.CurrentHolderId;
        Assert.NotEqual(before, after);
    }
}
