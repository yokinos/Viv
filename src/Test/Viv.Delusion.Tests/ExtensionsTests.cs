using System.Data;
using System.Text;
using Viv.Delusion;
using Viv.Delusion.Extension;
using Viv.Delusion.Magic;

namespace Viv.Delusion.Tests;

public class ExtensionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsNullOrEmpty空白为true(string? input)
        => Assert.True(input.IsNullOrEmpty());

    [Fact]
    public void IsNullOrEmpty非空为false()
        => Assert.False("x".IsNullOrEmpty());

    [Fact]
    public void ExtToString空值()
    {
        Assert.Equal(string.Empty, ((object?)null).ExtToString());
        Assert.Equal(string.Empty, DBNull.Value.ExtToString());
    }

    [Fact]
    public void ExtToString字节数组解码UTF8()
        => Assert.Equal("hello", Encoding.UTF8.GetBytes("hello").ExtToString());

    [Fact]
    public void ExtToString去除首尾空白()
        => Assert.Equal("hi", "  hi  ".ExtToString());

    [Fact]
    public void Between包含边界()
    {
        Assert.True(5.Between(1, 10));
        Assert.True(1.Between(1, 10));
        Assert.True(10.Between(1, 10));
        Assert.False(11.Between(1, 10));
    }

    [Fact]
    public void Nvl空值替换()
    {
        Assert.Equal("x", ((string?)null).Nvl("x"));
        Assert.Equal("x", "".Nvl("x"));
        Assert.Equal("a", "a".Nvl("x"));
    }

    [Fact]
    public void ToBytesnull返回默认()
        => Assert.Null(((object?)null!).ToBytes());

    [Fact]
    public void ToBytes对象转UTF8字节()
    {
        var bytes = "abc".ToBytes();
        Assert.Equal(Encoding.UTF8.GetBytes("abc"), bytes);
    }

    [Fact]
    public void ToJson空与字符串直通()
    {
        Assert.Equal(string.Empty, ((object?)null).ToJson());
        Assert.Equal("abc", "abc".ToJson());
    }

    [Fact]
    public void ToJson对象序列化()
        => Assert.Contains("Name", new { Name = "viv" }.ToJson());

    [Fact]
    public void DeserializeJson回环()
    {
        var dto = new PersonDto { Name = "viv", Age = 3 };
        var back = dto.ToJson().DeserializeJson<PersonDto>();
        Assert.Equal("viv", back!.Name);
        Assert.Equal(3, back.Age);
    }

    [Fact]
    public void DeserializeJson空字符串回默认()
    {
        Assert.Equal(7, "".DeserializeJson<int>(7));
        Assert.Equal(7, "  ".DeserializeJson<int>(7));
    }

    [Fact]
    public void DateTime格式化()
    {
        var dt = new DateTime(2026, 2, 11, 8, 30, 0);
        Assert.Equal("2026-02-11 08:30:00", dt.ExtToString());
        Assert.Equal("20260211", dt.ExtToString(DateFormat.ShortDate));
        Assert.Equal("2026/02/11", dt.ExtToString(DateFormat.Date, "/"));
        Assert.Equal("20260211083000", dt.ExtToString(DateFormat.CompactLongDate));
        Assert.Equal("083000", dt.ExtToString(DateFormat.Time));
        Assert.Equal("08:30:00", dt.ExtToString(DateFormat.StandardTime));
    }

    [Fact]
    public void DateTime极值返回空()
    {
        Assert.Equal(string.Empty, DateTime.MinValue.ExtToString());
        Assert.Equal(string.Empty, DateTime.MaxValue.ExtToString());
    }

    [Fact]
    public void ToUnixTime()
    {
        Assert.Equal(0L, new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToUnixTime());
        Assert.Equal(1000L, new DateTimeOffset(1970, 1, 1, 0, 0, 1, TimeSpan.Zero).ToUnixTime(true));
    }

    [Fact]
    public void DeepCopy独立副本()
    {
        var src = new PersonDto { Name = "viv", Age = 3 };
        var copy = src.DeepCopy()!;
        copy.Age = 99;
        Assert.Equal(3, src.Age);
        Assert.Equal("viv", copy.Name);
    }

    [Fact]
    public void GetAttribute取到与缺失()
    {
        Assert.NotNull(typeof(PersonDto).GetAttribute<MarkerAttribute>());
        Assert.Null(typeof(Color).GetAttribute<MarkerAttribute>());
    }

    [Fact]
    public void DataTable双向转换()
    {
        var list = new List<PersonDto>
        {
            new() { Name = "a", Age = 1 },
            new() { Name = "b", Age = 2 },
        };

        var dt = list.ToDataTable();
        Assert.Equal(2, dt!.Rows.Count);
        Assert.Equal(2, dt.Columns.Count);

        var back = dt.ToList<PersonDto>();
        Assert.Equal(2, back!.Count);
        Assert.Equal(1, back[0].Age);
        Assert.Equal("b", back[1].Name);
    }

    [Fact]
    public void DataTable空输入返回null()
    {
        Assert.Null(((IList<PersonDto>?)null).ToDataTable());
        Assert.Null(new DataTable().ToList<PersonDto>());
    }

    [Fact]
    public void ToDataTable列名按属性()
    {
        var dt = new[] { new PersonDto { Name = "x" } }.ToList().ToDataTable();
        var cols = DataTableMagic.GetColumnNames(dt!.Columns);
        Assert.Contains("Name", cols);
        Assert.Contains("Age", cols);
        Assert.Equal(2, cols.Count);
    }
}
