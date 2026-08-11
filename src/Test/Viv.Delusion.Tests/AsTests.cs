using System.Data;
using System.Text;
using Viv.Delusion.Extension;

namespace Viv.Delusion.Tests;

/// <summary>
/// As&lt;T&gt;（ObjectMapper.TryConvert）——Delusion 类型安全转换核心，用户点名必测。
/// 契约：失败/空值一律回默认值不抛异常；8 位纯数字优先按 yyyyMMdd 日期解析（避免误判 Unix 秒）。
/// </summary>
public class AsTests
{
    [Fact]
    public void null源返回默认值()
        => Assert.Equal(-1, ((object?)null).As<int>(-1));

    [Fact]
    public void DBNull源返回默认值()
        => Assert.Equal(0, DBNull.Value.As<int>());

    [Fact]
    public void 同类型直通()
        => Assert.Equal("abc", "abc".As<string>());

    [Fact]
    public void 字符串转数值()
    {
        Assert.Equal(42, "42".As<int>());
        Assert.Equal(42L, "42".As<long>());
        Assert.Equal(3.14m, "3.14".As<decimal>());
        Assert.Equal(2.5d, "2.5".As<double>());
        Assert.Equal('a', "a".As<char>());
        Assert.Equal((byte)7, "7".As<byte>());
    }

    [Fact]
    public void 原始数值类型间转换()
    {
        Assert.Equal(42L, 42.As<long>());
        Assert.Equal(42, 42L.As<int>());
        Assert.Equal(123, 123.45.As<int>()); // ChangeType 截断
    }

    [Fact]
    public void 数字转字符串()
        => Assert.Equal("42", 42.As<string>());

    [Fact]
    public void 布尔关键字识别()
    {
        foreach (var k in new[] { "是", "对", "正确", "YES", "OK", "1", "成功", "Y", "yes", "true" })
        {
            Assert.True(k.As<bool>(), $"关键字 {k} 应解析为 true");
        }
    }

    [Fact]
    public void 非法布尔字符串回默认()
        => Assert.False("否".As<bool>());

    [Fact]
    public void 字符串转枚举忽略大小写()
    {
        Assert.Equal(TestState.Active, "Active".As<TestState>());
        Assert.Equal(TestState.Active, "active".As<TestState>());
        Assert.Equal(TestState.Active, "2".As<TestState>()); // 数值字符串
    }

    [Fact]
    public void 数值转枚举()
        => Assert.Equal(TestState.Disabled, 4.As<TestState>());

    [Fact]
    public void 非法枚举字符串回默认()
        => Assert.Equal(TestState.None, "NoSuchState".As<TestState>());

    [Fact]
    public void 八位数字按yyyyMMdd日期解析()
    {
        // 20260211 若按 Unix 秒会落到 ~1970-08，8 位数字分支必须把它当日期
        Assert.Equal(new DateTime(2026, 2, 11), "20260211".As<DateTime>());
    }

    [Fact]
    public void 常规日期字符串解析()
    {
        Assert.Equal(new DateTime(2026, 2, 11), "2026-02-11".As<DateTime>());
        Assert.Equal(new DateTime(2026, 2, 11, 8, 30, 0), "2026-02-11 08:30:00".As<DateTime>());
    }

    [Fact]
    public void Unix秒时间戳解析()
    {
        long unix = 1770800000;
        var expected = DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;
        Assert.Equal(expected, unix.ToString().As<DateTime>());
    }

    [Fact]
    public void Unix毫秒时间戳解析()
    {
        long unixMs = 1770800000000L;
        var expected = DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime;
        Assert.Equal(expected, unixMs.ToString().As<DateTime>());
    }

    [Fact]
    public void DateTimeOffset解析()
    {
        Assert.Equal(new DateTimeOffset(2026, 2, 11, 0, 0, 0, TimeSpan.Zero), "20260211".As<DateTimeOffset>());
        var expected = DateTimeOffset.FromUnixTimeSeconds(1770800000).ToLocalTime();
        Assert.Equal(expected, "1770800000".As<DateTimeOffset>());
    }

    [Fact]
    public void Guid解析()
        => Assert.Equal(Guid.Parse("9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d"), "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d".As<Guid>());

    [Fact]
    public void 字符串转字节数组()
        => Assert.Equal(Encoding.UTF8.GetBytes("abc"), "abc".As<byte[]>());

    [Fact]
    public void 字节数组转字符串()
        => Assert.Equal("hi", new byte[] { 104, 105 }.As<string>());

    [Fact]
    public void 解析失败回默认值()
    {
        Assert.Equal(0, "abc".As<int>());
        Assert.Equal(-1, "abc".As<int>(-1));
        Assert.Equal(0m, "xyz".As<decimal>());
    }

    [Fact]
    public void 可空目标解析失败为null()
        => Assert.Null("abc".As<int?>());

    [Fact]
    public void 可空目标解析成功()
        => Assert.Equal(42, "42".As<int?>());

    [Fact]
    public void 不可解析日期回默认()
        => Assert.Equal(DateTime.MaxValue, "not-a-date".As<DateTime>(DateTime.MaxValue));

    [Fact]
    public void 对象映射为目标类型()
    {
        var src = new { Name = "viv", Age = 3 };
        var dst = src.As<PersonDto>();
        Assert.NotNull(dst);
        Assert.Equal("viv", dst!.Name);
        Assert.Equal(3, dst.Age);
    }
}
