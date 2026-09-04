namespace Viv.Redis.Tests;

/// <summary>
/// LockAutoRenewal 静态登记表跨测试共享，串行避免串 key。
/// </summary>
[CollectionDefinition("LockAutoRenewalStatic", DisableParallelization = true)]
public sealed class LockAutoRenewalStaticCollection;

[Collection("LockAutoRenewalStatic")]
public class LockAutoRenewalTests
{
    [Fact]
    public async Task Stop_立即取消不调用续期()
    {
        var key = UniqueKey();
        var calls = 0;
        LockAutoRenewal.Start(key, "h1", TimeSpan.FromMilliseconds(200), () =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(true);
        });
        LockAutoRenewal.Stop(key);
        await Task.Delay(300);

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task 续期返回false_只跑一轮后停止()
    {
        var key = UniqueKey();
        var calls = 0;
        LockAutoRenewal.Start(key, "h1", TimeSpan.FromMilliseconds(150), () =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(false);
        });
        await Task.Delay(450);
        LockAutoRenewal.Stop(key);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task 同持有者再次Start_复用循环不加倍()
    {
        var key = UniqueKey();
        var calls = 0;
        Func<Task<bool>> renew = () =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(false);
        };

        LockAutoRenewal.Start(key, "h1", TimeSpan.FromMilliseconds(150), renew);
        LockAutoRenewal.Start(key, "h1", TimeSpan.FromMilliseconds(150), renew);
        await Task.Delay(450);
        LockAutoRenewal.Stop(key);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task 不同持有者Start_替换旧循环()
    {
        var key = UniqueKey();
        var first = 0;
        var second = 0;

        LockAutoRenewal.Start(key, "old", TimeSpan.FromMilliseconds(150), () =>
        {
            Interlocked.Increment(ref first);
            return Task.FromResult(true);
        });
        LockAutoRenewal.Start(key, "new", TimeSpan.FromMilliseconds(150), () =>
        {
            Interlocked.Increment(ref second);
            return Task.FromResult(false);
        });
        await Task.Delay(450);
        LockAutoRenewal.Stop(key);

        Assert.Equal(0, first);
        Assert.Equal(1, second);
    }

    private static string UniqueKey() => "lock-renew:" + Guid.NewGuid().ToString("N");
}
