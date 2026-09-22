using Viv.Contracts;
using Viv.Contracts.Enums;

namespace Viv.Engine.Tests;

/// <summary>
/// 锁 Key 归一化 —— 原先埋在 <c>DistributedLockAccessor</c> 里是 private，一行都测不到。
/// 抽出来之后这里钉的是「Key 长什么样」这个对外可见的行为：换个前缀、换条拼装路径，
/// 锁就落到别的 Key 空间里去了，而 Redis 里什么都看不出来。
/// </summary>
public class LockKeyMagicTests
{
    [Fact]
    public void Join_按传入顺序冒号拼段()
    {
        var key = LockKeyMagic.Join(LockKeyMagic.NanaPrefix, "apex", nameof(VivConnType), 42);

        Assert.Equal("nana:apex:VivConnType:42", key);
    }

    [Fact]
    public void Join_段里的Null_当成空串不抛()
    {
        Assert.Equal("lock:a::c", LockKeyMagic.Join(LockKeyMagic.BusinessPrefix, "a", null, "c"));
    }

    [Fact]
    public void Generate_字符串原样返回_前缀不再加()
    {
        // string 直通是有意的：调用方自己拼前缀（消费锁的 nana:、缓存的 lock:），
        // 这里再加一次就成了 lock:nana:...，与取锁时那把对不上。
        Assert.Equal("biz:apex:1", LockKeyMagic.Generate("biz:apex:1"));
        Assert.Equal("x", LockKeyMagic.Generate("x", LockKeyMagic.NanaPrefix));
    }

    [Fact]
    public void Generate_Null_带前缀的null()
    {
        Assert.Equal("lock:null", LockKeyMagic.Generate(null));
        Assert.Equal("nana:null", LockKeyMagic.Generate(null, LockKeyMagic.NanaPrefix));
    }

    [Fact]
    public void Generate_值类型_直接ToString加前缀()
    {
        Assert.Equal("lock:42", LockKeyMagic.Generate(42L));
        Assert.Equal($"lock:{Guid.Empty}", LockKeyMagic.Generate(Guid.Empty));
    }

    [Fact]
    public void Generate_枚举_直接ToString加前缀()
    {
        Assert.Equal($"lock:{VivConnType.Redis}", LockKeyMagic.Generate(VivConnType.Redis));
    }

    [Fact]
    public void Generate_对象_按属性名字母序拼下划线()
    {
        // 声明顺序是 B、A，拼出来必须是 A_B —— 顺序由属性名定，不由声明位置定
        Assert.Equal("lock:1_2", LockKeyMagic.Generate(new { B = 2, A = 1 }));
    }

    [Fact]
    public void Generate_对象属性值为Null_写成null()
    {
        Assert.Equal("lock:a_null", LockKeyMagic.Generate(new { A = "a", B = (string?)null }));
    }

    [Fact]
    public void Generate_自定义前缀_换前缀不换归一化()
    {
        Assert.Equal("custom:42", LockKeyMagic.Generate(42L, "custom:"));
    }

    [Fact]
    public void Generate_无公开可读属性_退回ToString()
    {
        var key = LockKeyMagic.Generate(new NoReadableProps());

        Assert.StartsWith("lock:", key);
        Assert.Contains(nameof(NoReadableProps), key);
    }

    /// <summary>只读属性都没有的探针 —— 反射那条分支的兜底出路</summary>
    private sealed class NoReadableProps
    {
        public string WriteOnly { set { } }
    }
}
