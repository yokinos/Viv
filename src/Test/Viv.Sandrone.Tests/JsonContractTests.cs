using Newtonsoft.Json;
using Viv.Sandrone.Conveter;

namespace Viv.Sandrone.Tests;

/// <summary>
/// Sandrone 序列化契约：JsonConverterLong（long→字符串防前端精度丢失）、
/// NullToEmptyStringValueProvider（null 字符串→""、反序列化 Trim）、VivContractResolver。
/// </summary>
public class JsonContractTests
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new VivContractResolver(),
    };

    public class TestDto
    {
        public long Id { get; set; }
        public long? NullableId { get; set; }
        public string? Name { get; set; }
        public int Age { get; set; }
    }

    [Fact]
    public void Long序列化为字符串()
    {
        var json = JsonConvert.SerializeObject(new TestDto { Id = 1234567890123L }, Settings);
        Assert.Contains("\"Id\":\"1234567890123\"", json);
        Assert.DoesNotContain("\"Id\":1234567890123", json);
    }

    [Fact]
    public void Null字符串序列化为空串()
    {
        var json = JsonConvert.SerializeObject(new TestDto { Name = null }, Settings);
        Assert.Contains("\"Name\":\"\"", json);
    }

    [Fact]
    public void NullableLong为null保持null()
    {
        var json = JsonConvert.SerializeObject(new TestDto { NullableId = null }, Settings);
        Assert.Contains("\"NullableId\":null", json);
    }

    [Fact]
    public void NullableLong非null转字符串()
    {
        var json = JsonConvert.SerializeObject(new TestDto { NullableId = 999L }, Settings);
        Assert.Contains("\"NullableId\":\"999\"", json);
    }

    [Fact]
    public void 反序列化Trim字符串()
    {
        var dto = JsonConvert.DeserializeObject<TestDto>("{\"Name\":\"  hi  \"}", Settings);
        Assert.Equal("hi", dto!.Name);
    }

    [Fact]
    public void JsonConverterLong读写语义()
    {
        var converter = new JsonConverterLong();
        Assert.True(converter.CanConvert(typeof(long)));
        Assert.True(converter.CanConvert(typeof(long?)));
        Assert.False(converter.CanConvert(typeof(int)));

        // 读：空串 → 0 / null；非法 → 默认
        Assert.Equal("\"123\"", JsonConvert.SerializeObject(123L, converter));
        Assert.Equal(123L, JsonConvert.DeserializeObject<long>("\"123\"", converter));
        Assert.Equal(0L, JsonConvert.DeserializeObject<long>("\"\"", converter));
        Assert.Equal(0L, JsonConvert.DeserializeObject<long>("\"abc\"", converter));
        Assert.Null(JsonConvert.DeserializeObject<long?>("\"\"", converter));
    }
}
