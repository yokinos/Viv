using Viv.Delusion.Extension;
using Viv.Delusion.Magic;

namespace Viv.Delusion.Tests;

public class EnumMagicTests
{
    [Fact]
    public void EnumToList含描述()
    {
        var list = EnumMagic.EnumToList(typeof(Color));
        Assert.Equal(3, list!.Count);
        Assert.Equal("红色", list[0].Key);
        Assert.Equal(0, list[0].Value);
        Assert.Equal("绿色", list[1].Key);
        Assert.Equal(1, list[1].Value);
        Assert.Equal("Blue", list[2].Key); // 无描述回退字段名
        Assert.Equal(2, list[2].Value);
    }

    [Fact]
    public void EnumToList非枚举返回null()
        => Assert.Null(EnumMagic.EnumToList(typeof(string)));

    [Fact]
    public void GetDescription有描述与回退()
    {
        Assert.Equal("红色", EnumMagic.GetDescription(Color.Red));
        Assert.Equal("Blue", EnumMagic.GetDescription(Color.Blue));
        Assert.Equal(string.Empty, EnumMagic.GetDescription(null!));
    }

    [Fact]
    public void Parse按名称忽略大小写()
    {
        Assert.Equal(Color.Green, EnumMagic.Parse<Color>("green"));
        Assert.Equal(Color.Red, EnumMagic.Parse<Color>("RED"));
    }

    [Fact]
    public void Parse按数值()
    {
        Assert.Equal(Color.Green, EnumMagic.Parse<Color>(1));
        Assert.Equal(Color.Blue, EnumMagic.Parse<Color>("2"));
    }

    [Fact]
    public void Parse同类型直通()
        => Assert.Equal(Color.Red, EnumMagic.Parse<Color>(Color.Red));

    [Fact]
    public void Parse非法返回默认()
    {
        Assert.Equal(Color.Red, EnumMagic.Parse<Color>("NoSuchColor"));
        Assert.Equal(Color.Red, EnumMagic.Parse<Color>(""));
        Assert.Equal(Color.Red, EnumMagic.Parse<Color>(null!));
    }

    [Fact]
    public void GetDescription扩展方法()
        => Assert.Equal("绿色", Color.Green.GetDescription());
}
