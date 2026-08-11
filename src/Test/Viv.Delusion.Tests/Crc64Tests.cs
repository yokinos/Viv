using System.Text;
using Viv.Delusion.Magic;

namespace Viv.Delusion.Tests;

public class Crc64Tests
{
    [Fact]
    public void 相同输入相同哈希()
    {
        Assert.Equal(Crc64Magic.ComputeCrc64("viv-delusion"), Crc64Magic.ComputeCrc64("viv-delusion"));
    }

    [Fact]
    public void 空字符串与空白返回0()
    {
        Assert.Equal(0UL, Crc64Magic.ComputeCrc64(""));
        Assert.Equal(0UL, Crc64Magic.ComputeCrc64("  "));
        Assert.Equal(0UL, Crc64Magic.ComputeCrc64((string)null!));
    }

    [Fact]
    public void 空字节数组返回0()
    {
        Assert.Equal(0UL, Crc64Magic.ComputeCrc64(Array.Empty<byte>()));
        Assert.Equal(0UL, Crc64Magic.ComputeCrc64((byte[]?)null!));
    }

    [Fact]
    public void 不同输入哈希不同()
        => Assert.NotEqual(Crc64Magic.ComputeCrc64("a"), Crc64Magic.ComputeCrc64("b"));

    [Fact]
    public void 字节数组ECMA已知向量()
    {
        // CRC-64/ECMA 反射实现标准测试向量："123456789" → 0x995DC9BBDF1939FA
        var crc = Crc64Magic.ComputeCrc64(Encoding.ASCII.GetBytes("123456789"));
        Assert.Equal(0x995DC9BBDF1939FAUL, crc);
    }
}
