using System.Diagnostics;
using System.Diagnostics.Metrics;
using Viv.Contracts.Enums;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Engine.Metrics;

namespace Viv.Engine.Tests;

/// <summary>
/// 指标读取。MeterListener 只收「起来之后」的测量，历史补不回来，所以每个测试都是
/// 先 new VivMeter 再记数。
///
/// 用独立前缀（Viv.TestProbe.）是为了让断言可以是精确值：xUnit 并行下别的测试也会往
/// Viv. 开头的 meter 上记数，而这里的 Count / Sum 绕不开精确断言，不能靠 Contains 混过去。
/// </summary>
public class VivMeterTests
{
    private const string Prefix = "Viv.TestProbe.";

    [Fact]
    public void 计数按标签累计()
    {
        using var viv = new VivMeter(Prefix);
        using var meter = new Meter(Prefix + "Counter");
        var calls = meter.CreateCounter<long>("viv.testprobe.calls");

        calls.Add(1);
        calls.Add(2);
        calls.Add(3);

        var series = One(viv, "viv.testprobe.calls");

        Assert.Equal(VivMeterKind.Counter, series.Kind);
        Assert.Equal(3L, series.Count);
        Assert.Equal(6d, series.Sum);
        Assert.Empty(series.Tags);
    }

    [Fact]
    public void 同一个仪表下标签不同就是两条序列()
    {
        using var viv = new VivMeter(Prefix);
        using var meter = new Meter(Prefix + "Tags");
        var calls = meter.CreateCounter<long>("viv.testprobe.tagged");

        calls.Add(1, Tag("result", "ok"));
        calls.Add(1, Tag("result", "error"));
        calls.Add(2, Tag("result", "ok"));

        var ok = One(viv, "viv.testprobe.tagged", "result", "ok");
        var error = One(viv, "viv.testprobe.tagged", "result", "error");

        Assert.Equal(2L, ok.Count);
        Assert.Equal(3d, ok.Sum);
        Assert.Equal(1L, error.Count);
        Assert.Equal(1d, error.Sum);
    }

    [Fact]
    public void 直方图记count_sum_min_max()
    {
        using var viv = new VivMeter(Prefix);
        using var meter = new Meter(Prefix + "Histogram");
        var duration = meter.CreateHistogram<double>("viv.testprobe.duration", "ms");

        duration.Record(5);
        duration.Record(15);
        duration.Record(10);

        var series = One(viv, "viv.testprobe.duration");

        Assert.Equal(VivMeterKind.Histogram, series.Kind);
        Assert.Equal("ms", series.Unit);
        Assert.Equal(3L, series.Count);
        Assert.Equal(30d, series.Sum);
        Assert.Equal(5d, series.Min);
        Assert.Equal(15d, series.Max);
    }

    [Fact]
    public void 水位类仪表是覆盖不是累加()
    {
        using var viv = new VivMeter(Prefix);
        using var meter = new Meter(Prefix + "Gauge");

        var level = 42L;
        meter.CreateObservableGauge("viv.testprobe.pending", () => Volatile.Read(ref level));

        level = 7;
        Assert.Equal(7d, One(viv, "viv.testprobe.pending").Sum);

        level = 99;
        var series = One(viv, "viv.testprobe.pending");

        Assert.Equal(VivMeterKind.Gauge, series.Kind);
        Assert.Equal(1L, series.Count);
        Assert.Equal(99d, series.Sum);
        Assert.Equal(99d, series.Max);
    }

    /// <summary>
    /// 回调注册了七种数值类型，只注册 long / double 的话这里会空着 —— 而那是静默的
    /// </summary>
    [Fact]
    public void 非long的数值类型也收得到()
    {
        using var viv = new VivMeter(Prefix);
        using var meter = new Meter(Prefix + "Int");
        var calls = meter.CreateCounter<int>("viv.testprobe.int");

        calls.Add(5);

        Assert.Equal(5d, One(viv, "viv.testprobe.int").Sum);
    }

    [Fact]
    public void 清零返回清零前的值且此后从零开始()
    {
        using var viv = new VivMeter(Prefix);
        using var meter = new Meter(Prefix + "Reset");
        var calls = meter.CreateCounter<long>("viv.testprobe.reset");

        calls.Add(3);

        var before = One(viv.Reset(), "viv.testprobe.reset");
        Assert.Equal(3d, before.Sum);

        calls.Add(4);
        var after = One(viv, "viv.testprobe.reset");

        Assert.Equal(1L, after.Count);
        Assert.Equal(4d, after.Sum);
    }

    [Fact]
    public void 前缀之外的meter不收()
    {
        using var viv = new VivMeter(Prefix);
        using var meter = new Meter("Other.Probe");
        meter.CreateCounter<long>("other.probe.calls").Add(1);

        Assert.Empty(viv.Meters);
        Assert.Empty(viv.Snapshot());
    }

    [Fact]
    public void Meters列出已收的meter名()
    {
        using var viv = new VivMeter(Prefix);
        using var meter = new Meter(Prefix + "Listed");
        meter.CreateCounter<long>("viv.testprobe.listed");

        Assert.Equal(new[] { Prefix + "Listed" }, viv.Meters);
    }

    /// <summary>
    /// 仪表被释放后测量静默失效（Add 不抛），留在账上就是一条冻在最后一帧的值。
    /// 摘掉它，让它从快照里消失，是个看得见的信号
    /// </summary>
    [Fact]
    public void 仪表被释放后不再留在快照里()
    {
        using var viv = new VivMeter(Prefix);
        var meter = new Meter(Prefix + "Disposed");
        meter.CreateCounter<long>("viv.testprobe.disposed").Add(1);

        Assert.Single(viv.Snapshot());

        meter.Dispose();

        Assert.Empty(viv.Snapshot());
    }

    private static KeyValuePair<string, object?> Tag(string key, string value)
    {
        return new KeyValuePair<string, object?>(key, value);
    }

    private static VivMeterSnapshot One(IVivMeter meter, string instrument, string? tagKey = null, string? tagValue = null)
    {
        return One(meter.Snapshot(), instrument, tagKey, tagValue);
    }

    private static VivMeterSnapshot One(IReadOnlyList<VivMeterSnapshot> snapshot, string instrument, string? tagKey = null, string? tagValue = null)
    {
        var matched = snapshot
            .Where(x => x.Instrument == instrument
                && (tagKey == null || (x.Tags.TryGetValue(tagKey, out var value) && value == tagValue)))
            .ToList();

        return Assert.Single(matched);
    }
}
