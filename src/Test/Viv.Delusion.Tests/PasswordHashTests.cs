using System.Security.Cryptography;
using Viv.Delusion.Magic;
using Viv.Delusion.Security;

namespace Viv.Delusion.Tests;

public class PasswordHashTests
{
    [Fact]
    public void 新格式哈希可以验证且无需升级()
    {
        var (hash, salt) = PasswordHash.Hash("P@ssw0rd!");

        Assert.True(PasswordHash.Verify(hash, salt, "P@ssw0rd!", out var needsRehash));
        Assert.False(needsRehash);
        Assert.StartsWith("pbkdf2-sha256$", hash, StringComparison.Ordinal);
        Assert.NotEqual("P@ssw0rd!", hash);
    }

    [Fact]
    public void 错误口令不通过()
    {
        var (hash, salt) = PasswordHash.Hash("P@ssw0rd!");

        Assert.False(PasswordHash.Verify(hash, salt, "P@ssw0rd", out var needsRehash));
        Assert.False(needsRehash);
    }

    [Fact]
    public void 同一口令两次哈希结果不同_盐随机()
    {
        var (hash1, salt1) = PasswordHash.Hash("same");
        var (hash2, salt2) = PasswordHash.Hash("same");

        Assert.NotEqual(salt1, salt2);
        Assert.NotEqual(hash1, hash2);
        Assert.True(PasswordHash.Verify(hash1, salt1, "same", out _));
        Assert.True(PasswordHash.Verify(hash2, salt2, "same", out _));
    }

    [Fact]
    public void 旧MD5格式一律验证失败_不再保留弱算法通道()
    {
        const string salt = "0123456789abcdef0123456789abcdef";
        const string password = "legacy-pass";
        var legacyHash = EncryptMagic.HashMd5(password + salt);

        // 口令本身是对的，但格式不是 PBKDF2，也必须拒绝。
        // 这条一旦变红，说明有人又把 MD5 兼容分支加回来了。
        Assert.False(PasswordHash.Verify(legacyHash, salt, password, out var needsRehash));
        Assert.False(needsRehash);
    }

    [Fact]
    public void 迭代次数偏低的老哈希验证后要求升级()
    {
        const string password = "old-iterations";
        var saltBytes = RandomNumberGenerator.GetBytes(16);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, 1_000, HashAlgorithmName.SHA256, 32);
        var hash = $"pbkdf2-sha256$1000${Convert.ToBase64String(key)}";

        Assert.True(PasswordHash.Verify(hash, Convert.ToBase64String(saltBytes), password, out var needsRehash));
        Assert.True(needsRehash);
    }

    [Fact]
    public void 迭代次数超上限_直接拒绝_避免被篡改的哈希拖垮CPU()
    {
        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var key = Convert.ToBase64String(new byte[32]);
        var hash = $"pbkdf2-sha256${PasswordHash.DefaultIterations * 20 + 1}${key}";

        Assert.False(PasswordHash.Verify(hash, salt, "pwd", out var needsRehash));
        Assert.False(needsRehash);
    }

    [Theory]
    [InlineData(8)]   // 过短：数据损坏
    [InlineData(128)] // 过长：同样是无效数据
    public void 派生密钥长度异常_拒绝验证(int keySize)
    {
        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var hash = $"pbkdf2-sha256$100000${Convert.ToBase64String(new byte[keySize])}";

        Assert.False(PasswordHash.Verify(hash, salt, "pwd", out var needsRehash));
        Assert.False(needsRehash);
    }

    [Theory]
    [InlineData(null, "salt", "pwd")]
    [InlineData("pbkdf2-sha256$abc$zzz", "not-base64!", "pwd")]
    [InlineData("pbkdf2-sha256$100000$", "c2FsdA==", "pwd")]
    public void 格式异常一律验证失败(string? hash, string? salt, string password)
    {
        Assert.False(PasswordHash.Verify(hash, salt, password, out var needsRehash));
        Assert.False(needsRehash);
    }
}
