using System.Security.Cryptography;
using System.Text;
using Viv.Delusion;
using Viv.Delusion.Magic;

namespace Viv.Delusion.Tests;

/// <summary>
/// EncryptMagic —— 哈希 + 对称加解密。契约：加密失败返回 null 不抛异常；
/// AES 走 encrypt-then-MAC（随机 IV + HMAC 认证），同明文密文不同、篡改拒绝。
/// </summary>
public class EncryptMagicTests
{
    [Fact]
    public void MD5已知向量()
        => Assert.Equal("e10adc3949ba59abbe56e057f20f883e", EncryptMagic.HashMd5("123456"));

    [Fact]
    public void SHA256为Base64编码()
    {
        var actual = EncryptMagic.HashSHA256("hello");
        var expected = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("hello")));
        Assert.Equal(expected, actual);
        Assert.Equal(44, actual.Length); // 256bit → Base64 44 字符
    }

    [Fact]
    public void 哈希null输入抛异常()
    {
        Assert.Throws<ArgumentNullException>(() => EncryptMagic.HashMd5(null!));
        Assert.Throws<ArgumentNullException>(() => EncryptMagic.HashSHA256(null!));
    }

    [Fact]
    public void AES回环()
    {
        const string key = "viv-encrypt-key";
        const string plain = "敏感数据-测试";
        var cipher = EncryptMagic.EncryptAES(key, plain);
        Assert.NotNull(cipher);
        Assert.NotEqual(plain, cipher);
        Assert.Equal(plain, EncryptMagic.DecryptAES(key, cipher!));
    }

    [Fact]
    public void AES密钥不匹配解密为null()
    {
        var cipher = EncryptMagic.EncryptAES("key-a", "hello");
        Assert.Null(EncryptMagic.DecryptAES("key-b", cipher!));
    }

    [Fact]
    public void AES密文被篡改解密为null()
    {
        var cipher = EncryptMagic.EncryptAES("key", "hello")!;
        var tampered = (cipher[0] == 'A' ? 'B' : 'A') + cipher[1..];
        Assert.Null(EncryptMagic.DecryptAES("key", tampered));
    }

    [Fact]
    public void 空密钥或明文返回null()
    {
        Assert.Null(EncryptMagic.EncryptAES("", "text"));
        Assert.Null(EncryptMagic.EncryptAES("key", ""));
        Assert.Null(EncryptMagic.DecryptAES("key", ""));
    }

    [Fact]
    public void DES与3DES回环()
    {
        const string plain = "des-text";
        Assert.Equal(plain, EncryptMagic.DecryptDES("key", EncryptMagic.EncryptDES("key", plain)!));
        Assert.Equal(plain, EncryptMagic.Decrypt3DES("key", EncryptMagic.Encrypt3DES("key", plain)!));
    }

    [Fact]
    public void 同一明文两次加密密文不同()
    {
        var a = EncryptMagic.EncryptAES("key", "fixed-plain");
        var b = EncryptMagic.EncryptAES("key", "fixed-plain");
        Assert.NotEqual(a, b); // 随机 IV
    }

    [Fact]
    public void DecryptRaw第三方格式解密()
    {
        var key = Convert.FromHexString("0123456789ABCDEF0123456789ABCDEF");
        var iv = Convert.FromHexString("000102030405060708090A0B0C0D0E0F");
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        var plain = Encoding.UTF8.GetBytes("third-party-plain");
        using var encryptor = aes.CreateEncryptor();
        var cipher = Convert.ToBase64String(encryptor.TransformFinalBlock(plain, 0, plain.Length));

        var result = EncryptMagic.DecryptRaw(EncrypType.AES, key, iv, cipher, CipherMode.CBC, PaddingMode.PKCS7);
        Assert.Equal("third-party-plain", result);
    }
}
