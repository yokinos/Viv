using System.Text;
using Viv.Delusion.Magic;

namespace Viv.Delusion.Tests;

public class Crc64Tests
{
    [Fact]
    public void 相同输入相同哈希()
    {
        Assert.Equal(HashMagic.Compute(HashMode.Crc64, "viv-delusion"), HashMagic.Compute(HashMode.Crc64, "viv-delusion"));
    }

    [Fact]
    public void 空字符串与空白返回0()
    {
        Assert.Equal(0UL, HashMagic.Compute(HashMode.Crc64, ""));
        Assert.Equal(0UL, HashMagic.Compute(HashMode.Crc64, "  "));
        Assert.Equal(0UL, HashMagic.Compute(HashMode.Crc64, (string)null!));
    }

    [Fact]
    public void 空字节数组返回0()
    {
        Assert.Equal(0UL, HashMagic.Compute(HashMode.Crc64, Array.Empty<byte>()));
        Assert.Equal(0UL, HashMagic.Compute(HashMode.Crc64, (byte[]?)null!));
    }

    [Fact]
    public void 不同输入哈希不同()
        => Assert.NotEqual(HashMagic.Compute(HashMode.Crc64, "a"), HashMagic.Compute(HashMode.Crc64, "b"));

    [Fact]
    public void 字节数组ECMA已知向量()
    {
        // CRC-64/ECMA-182 标准 check 值："123456789" → 0x6C40DF5F0B497347
        // （0x995DC9BBDF1939FA 是 CRC-64/XZ 的 check 值，为已移除的旧自定义实现的语义）
        var crc = HashMagic.Compute(HashMode.Crc64, Encoding.ASCII.GetBytes("123456789"));
        Assert.Equal(0x6C40DF5F0B497347UL, crc);
    }

    [Fact]
    public void 字节数组Xz已知向量()
    {
        // CRC-64/XZ 标准 check 值："123456789" → 0x995DC9BBDF1939FA
        var crc = HashMagic.Compute(HashMode.Crc64Xz, Encoding.ASCII.GetBytes("123456789"));
        Assert.Equal(0x995DC9BBDF1939FAUL, crc);
    }

    [Fact]
    public void Xz与Ecma同输入结果不同()
    {
        // 同一多项式、不同参数化，两者结果不通用；防止日后误把 Crc64Xz 接到 BCL 的 Crc64 上
        var bytes = Encoding.ASCII.GetBytes("123456789");
        Assert.NotEqual(
            HashMagic.Compute(HashMode.Crc64, bytes),
            HashMagic.Compute(HashMode.Crc64Xz, bytes));
    }

    [Fact]
    public void Xz字符串按UTF16LE计算()
    {
        // 与 Python 参考实现交叉验证，同时固化字符串→UTF-16 LE 的编码约定
        Assert.Equal(0x177AEBF7EEE14A5BUL, HashMagic.Compute(HashMode.Crc64Xz, "viv-delusion"));
        Assert.Equal(0x374E964DA1337897UL, HashMagic.Compute(HashMode.Crc64Xz, "tenant:1001"));
        Assert.Equal(0xD7602CCDEF615B2EUL, HashMagic.Compute(HashMode.Crc64Xz, "a"));
    }

    [Fact]
    public void Xz空输入返回0()
    {
        Assert.Equal(0UL, HashMagic.Compute(HashMode.Crc64Xz, ""));
        Assert.Equal(0UL, HashMagic.Compute(HashMode.Crc64Xz, Array.Empty<byte>()));
    }
}
