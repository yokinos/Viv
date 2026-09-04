namespace Viv.Redis.Tests;

/// <summary>
/// 锁值归属：非重入用原文精确匹配，重入用 holder + '\n' + count。
/// 旧方案贪婪剥 <c>_N</c> 会把 holder=<c>order-123_1</c> 认成 <c>order-123</c>。
/// </summary>
public class RedisLockScriptsTests
{
    [Fact]
    public void 非重入_holder含下划线数字_自己命中()
    {
        Assert.True(RedisLockScripts.OwnedBy("order-123_1", "order-123_1"));
    }

    [Fact]
    public void 非重入_holder含下划线数字_不被剥后缀后的短holder命中()
    {
        Assert.False(RedisLockScripts.OwnedBy("order-123_1", "order-123"));
    }

    [Fact]
    public void 重入_换行计数_命中原holder()
    {
        var encoded = RedisLockScripts.EncodeReentrant("order-123_1", 2);
        Assert.Equal("order-123_1\n2", encoded);
        Assert.True(RedisLockScripts.OwnedBy(encoded, "order-123_1"));
        Assert.False(RedisLockScripts.OwnedBy(encoded, "order-123"));
    }

    [Fact]
    public void 重入_普通holder_计数递增仍命中()
    {
        Assert.True(RedisLockScripts.OwnedBy(RedisLockScripts.EncodeReentrant("abc", 1), "abc"));
        Assert.True(RedisLockScripts.OwnedBy(RedisLockScripts.EncodeReentrant("abc", 3), "abc"));
        Assert.False(RedisLockScripts.OwnedBy(RedisLockScripts.EncodeReentrant("abc", 1), "other"));
    }

    [Fact]
    public void 旧下划线编码_不再当作重入值()
    {
        Assert.False(RedisLockScripts.OwnedBy("abc_1", "abc"));
        Assert.True(RedisLockScripts.OwnedBy("abc_1", "abc_1"));
    }

    [Fact]
    public void Lua脚本_加锁释放续期共用换行分隔与owned_by()
    {
        Assert.Contains("owned_by", RedisLockScripts.ReentrantAcquire, StringComparison.Ordinal);
        Assert.Contains("owned_by", RedisLockScripts.ReentrantRelease, StringComparison.Ordinal);
        Assert.Contains("owned_by", RedisLockScripts.Renew, StringComparison.Ordinal);
        Assert.Contains(@"^(.*)\n%d+$", RedisLockScripts.OwnedByFn, StringComparison.Ordinal);
        Assert.DoesNotContain("_%d+$", RedisLockScripts.ReentrantAcquire, StringComparison.Ordinal);
        Assert.DoesNotContain("_%d+$", RedisLockScripts.ReentrantRelease, StringComparison.Ordinal);
        Assert.DoesNotContain("_%d+$", RedisLockScripts.Renew, StringComparison.Ordinal);
    }
}
