using System.ComponentModel;

namespace Viv.Delusion.Tests;

/// <summary>测试枚举：部分项带 Description，验证 EnumMagic / As&lt;T&gt; 的枚举转换。</summary>
public enum TestState
{
    [Description("无")] None = 0,
    [Description("启用")] Active = 2,
    [Description("停用")] Disabled = 4,
}

public enum Color
{
    [Description("红色")] Red = 0,
    [Description("绿色")] Green = 1,
    Blue = 2, // 无描述 → 回退字段名
}

[Marker]
public class PersonDto
{
    public string? Name { get; set; }
    public int Age { get; set; }
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class MarkerAttribute : Attribute { }
