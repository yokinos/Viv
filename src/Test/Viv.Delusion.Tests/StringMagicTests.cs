using Viv.Delusion.Magic;

namespace Viv.Delusion.Tests;

public class StringMagicTests
{
    [Theory]
    [InlineData("test@example.com", true)]
    [InlineData("a.b+tag@sub.domain.org", true)]
    [InlineData("not-an-email", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsEmail(string? input, bool expected)
        => Assert.Equal(expected, StringMagic.IsEmail(input!));

    [Theory]
    [InlineData("13800138000", true)]
    [InlineData("12345678901", false)] // 第二位必须 3-9
    [InlineData("1380013800", false)]  // 10 位
    [InlineData("abc", false)]
    public void IsMobile(string? input, bool expected)
        => Assert.Equal(expected, StringMagic.IsMobile(input!));

    [Theory]
    [InlineData("192.168.1.1", true)]
    [InlineData("255.255.255.0", true)]
    [InlineData("256.1.1.1", false)]
    [InlineData("1.2.3", false)]
    [InlineData(null, false)]
    public void IsIPV4Address(string? input, bool expected)
        => Assert.Equal(expected, StringMagic.IsIPV4Address(input!));

    [Theory]
    [InlineData("123", true)]
    [InlineData("12.3", false)] // IsNumber 不含小数
    [InlineData("-5", false)]
    [InlineData("abc", false)]
    public void IsNumber(string? input, bool expected)
        => Assert.Equal(expected, StringMagic.IsNumber(input!));

    [Theory]
    [InlineData("12.3", true)]
    [InlineData("123", true)]
    [InlineData("1.2.3", false)]
    [InlineData("abc", false)]
    public void IsDecimal(string? input, bool expected)
        => Assert.Equal(expected, StringMagic.IsDecimal(input!));

    [Theory]
    [InlineData("中文", true)]
    [InlineData("abc", false)]
    [InlineData("中a", false)]
    public void IsChinese(string? input, bool expected)
        => Assert.Equal(expected, StringMagic.IsChinese(input!));

    [Theory]
    [InlineData("hello", true)]
    [InlineData("中文", false)]
    [InlineData("h3llo", false)]
    public void IsEnglish(string? input, bool expected)
        => Assert.Equal(expected, StringMagic.IsEnglish(input!));

    [Theory]
    [InlineData("11010519491231002X", true)]
    [InlineData("110105194912310021", false)] // 校验位错误
    [InlineData("123456789012345678", false)]
    [InlineData("abc", false)]
    public void IsIDCard(string? input, bool expected)
        => Assert.Equal(expected, StringMagic.IsIDCard(input!));

    [Fact]
    public void Url编码解码回环()
    {
        const string input = "a b&c=中文/测试?x=1";
        var encoded = StringMagic.UrlEncode(input);
        Assert.NotEqual(input, encoded);
        Assert.Equal(input, StringMagic.UrlDecode(encoded));
        Assert.Equal(string.Empty, StringMagic.UrlEncode(null!));
        Assert.Equal(string.Empty, StringMagic.UrlDecode(null!));
    }

    [Fact]
    public void 首字母大小写()
    {
        Assert.Equal("userName", StringMagic.FirstLowerCase("UserName"));
        Assert.Equal("a", StringMagic.FirstLowerCase("A"));
        Assert.Equal(string.Empty, StringMagic.FirstLowerCase(""));
        Assert.Equal("UserName", StringMagic.FirstUpperCase("userName"));
        Assert.Equal("A", StringMagic.FirstUpperCase("a"));
    }

    [Fact]
    public void 驼峰拆分()
    {
        Assert.Equal("user-name", StringMagic.SplitCamelCase("UserName"));
        Assert.Equal("viv-delusion", StringMagic.SplitCamelCase("VivDelusion"));
        Assert.Equal(string.Empty, StringMagic.SplitCamelCase("  "));
    }

    [Fact]
    public void 移除首尾字符串()
    {
        Assert.Equal("World", StringMagic.RemoveStart("HelloWorld", "hello")); // 忽略大小写
        Assert.Equal("HelloWorld", StringMagic.RemoveStart("HelloWorld", "xyz"));
        Assert.Equal("Hello", StringMagic.RemoveEnd("HelloWorld", "world"));
        Assert.Equal("HelloWorld", StringMagic.RemoveEnd("HelloWorld", "xyz"));
        Assert.Equal(string.Empty, StringMagic.RemoveEnd(null!, "x"));
        Assert.Equal("text", StringMagic.RemoveEnd("text", null!)); // 空 removeValue 原样返回
    }

    [Fact]
    public void 文件大小格式化()
    {
        Assert.Equal("2.00 KB", StringMagic.ToReadableFileSize(2048));
        Assert.Equal("500.00 Byte", StringMagic.ToReadableFileSize(500));
        Assert.Equal("0.00 Byte", StringMagic.ToReadableFileSize(0));
        Assert.Equal("0.00 Byte", StringMagic.ToReadableFileSize(-5));
    }

    [Fact]
    public void GenerateSecureString长度与字符集()
    {
        var s = StringMagic.GenerateSecureString(16);
        Assert.Equal(16, s.Length);
        Assert.All(s, c => Assert.True(char.IsAsciiDigit(c) || (c is >= 'a' and <= 'z')));
    }

    [Fact]
    public void GenerateSecureString指定大写字符集()
    {
        var s = StringMagic.GenerateSecureString(8, useNumber: false, useLower: false, useUpper: true);
        Assert.Equal(8, s.Length);
        Assert.All(s, c => Assert.True(c is >= 'A' and <= 'Z'));
    }

    [Fact]
    public void GenerateSecureString长度非正返回空()
        => Assert.Equal(string.Empty, StringMagic.GenerateSecureString(0));

    [Fact]
    public void JsonFormat格式化与容错()
    {
        Assert.Contains('\n', StringMagic.JsonFormat("{\"a\":1,\"b\":[1,2]}"));
        var compact = StringMagic.JsonFormat("{\"a\":1}", 0); // 缩进0：仍换行，仅无缩进空格
        Assert.NotEqual("{\"a\":1}", compact);
        Assert.Contains("\"a\"", compact);
        Assert.Equal("bad-json", StringMagic.JsonFormat("bad-json"));
        Assert.Equal(string.Empty, StringMagic.JsonFormat(null));
    }

    [Fact]
    public void 中文大写金额零值()
        => Assert.Equal("零元整", StringMagic.ToChineseAmount(0));
}
