using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Viv.Contracts.Enums;
using Viv.Fakes;
using Viv.Momo;

namespace Viv.Momo.Tests;

/// <summary>
/// 数据库指标。与 RedisMetricsTests 同形：仪器名逐字钉住，Record* 用 MeterListener 确认出得来。
/// </summary>
public class MomoMetricsTests
{
    [Fact]
    public void 仪器名稳定()
    {
        Assert.Equal("Viv.Momo", MomoMetrics.MeterName);

        Assert.Equal("viv.momo.query.duration", MomoMetrics.QueryDurationInstrument);
        Assert.Equal("viv.momo.query.slow", MomoMetrics.SlowQueryInstrument);
        Assert.Equal("viv.momo.errors", MomoMetrics.ErrorsInstrument);
        Assert.Equal("viv.momo.batch.path", MomoMetrics.BatchPathInstrument);
    }

    [Fact]
    public void RecordBatchPath带path与op标签()
    {
        var measurements = new List<(string Path, string Op)>();
        using var listener = ListenLong(MomoMetrics.BatchPathInstrument, tags => measurements.Add((Tag(tags, "path"), Tag(tags, "op"))));

        MomoMetrics.RecordBatchPath("ef", "insert");
        MomoMetrics.RecordBatchPath("dapper", "update");

        Assert.Contains(("ef", "insert"), measurements);
        Assert.Contains(("dapper", "update"), measurements);
    }

    [Fact]
    public void RecordError带库类型标签()
    {
        var types = new List<string>();
        using var listener = ListenLong(MomoMetrics.ErrorsInstrument, tags => types.Add(Tag(tags, "type")));

        MomoMetrics.RecordError(VivConnType.PostgreSQL);
        MomoMetrics.RecordError(VivConnType.SqlServer);

        Assert.Contains("postgresql", types);
        Assert.Contains("sqlserver", types);
    }

    private static MeterListener ListenLong(string instrumentName, Action<List<KeyValuePair<string, object?>>> onMeasurement)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == MomoMetrics.MeterName && instrument.Name == instrumentName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) => onMeasurement(tags.ToArray().ToList()));
        listener.Start();
        return listener;
    }

    private static string Tag(List<KeyValuePair<string, object?>> tags, string key)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == key)
                return tag.Value?.ToString() ?? string.Empty;
        }

        return string.Empty;
    }
}

/// <summary>
/// 慢查询判据。EF 拦截器与 34 条 Dapper 命令都走它，所以阈值分支与 SQL 截断在这里验一次就够，
/// 两条路各自不用再验一遍。
///
/// Measure / MeasureAsync 是 Dapper 34 个执行点共用的计时形状，这里只验它自己没记错，
/// 验不了 34 处是否都套上了，那要连真库。
/// </summary>
public class QueryTelemetryTests
{
    [Fact]
    public void 不超阈值时不记慢查询也不记日志()
    {
        var logger = new RecordingLogger();
        var telemetry = new QueryTelemetry(logger, 100);

        var slow = RecordAndCountSlow(telemetry, "ef", "reader", 99, "SELECT 1");

        Assert.Equal(0, slow);
        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public void 超阈值时记慢查询并记日志()
    {
        var logger = new RecordingLogger();
        var telemetry = new QueryTelemetry(logger, 100);

        var slow = RecordAndCountSlow(telemetry, "ef", "reader", 100, "SELECT 1");

        Assert.Equal(1, slow);

        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("100ms", warning);
        Assert.Contains("path:ef", warning);
        Assert.Contains("op:reader", warning);
    }

    [Fact]
    public void 阈值配成0等于关闭()
    {
        var logger = new RecordingLogger();
        var telemetry = new QueryTelemetry(logger, 0);

        var slow = RecordAndCountSlow(telemetry, "dapper", "page", 99999, "SELECT 1");

        Assert.Equal(0, slow);
        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public void 慢查询日志里的SQL截到200字符()
    {
        var logger = new RecordingLogger();
        var telemetry = new QueryTelemetry(logger, 100);
        var sql = new string('x', 300);

        RecordAndCountSlow(telemetry, "dapper", "page", 500, sql);

        var warning = Assert.Single(logger.Warnings);
        Assert.Contains(new string('x', 200) + "...", warning);
        Assert.DoesNotContain(new string('x', 201), warning);
    }

    [Fact]
    public void 空SQL记成占位符而不是空着()
    {
        var logger = new RecordingLogger();
        var telemetry = new QueryTelemetry(logger, 100);

        RecordAndCountSlow(telemetry, "dapper", "page", 500, null);

        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("SQL:<none>", warning);
    }

    [Fact]
    public void Measure成功时记一条耗时并返回委托的值()
    {
        var telemetry = new QueryTelemetry(new RecordingLogger(), 100);
        var samples = new List<(double Value, string Path, string Op)>();
        using var listener = ListenDapperDuration(samples);

        var result = telemetry.Measure("dapper", "reader", "SELECT * FROM T", () => 42);

        Assert.Equal(42, result);

        var sample = Assert.Single(samples);
        Assert.Equal("dapper", sample.Path);
        Assert.Equal("reader", sample.Op);
    }

    [Fact]
    public async Task MeasureAsync成功时记一条耗时并返回委托的值()
    {
        var telemetry = new QueryTelemetry(new RecordingLogger(), 100);
        var samples = new List<(double Value, string Path, string Op)>();
        using var listener = ListenDapperDuration(samples);

        var result = await telemetry.MeasureAsync("dapper", "scalar", "SELECT 1", async () =>
        {
            await Task.Yield();
            return 42;
        });

        Assert.Equal(42, result);

        var sample = Assert.Single(samples);
        Assert.Equal("dapper", sample.Path);
        Assert.Equal("scalar", sample.Op);
    }

    /// <summary>
    /// 记录放在 finally 就是为了这个。Assert.Same 钉住是同一个实例，
    /// 被包一层之后调用方原来的 catch 会全部失配
    /// </summary>
    [Fact]
    public void Measure在委托抛异常时也记一条并把原异常照常上抛()
    {
        var telemetry = new QueryTelemetry(new RecordingLogger(), 100);
        var samples = new List<(double Value, string Path, string Op)>();
        using var listener = ListenDapperDuration(samples);
        var boom = new InvalidOperationException("炸了");

        var thrown = Assert.Throws<InvalidOperationException>(
            () => telemetry.Measure<int>("dapper", "insert", "INSERT INTO T VALUES (1)", () => throw boom));

        Assert.Same(boom, thrown);
        Assert.Single(samples);
    }

    [Fact]
    public async Task MeasureAsync在委托抛异常时也记一条并把原异常照常上抛()
    {
        var telemetry = new QueryTelemetry(new RecordingLogger(), 100);
        var samples = new List<(double Value, string Path, string Op)>();
        using var listener = ListenDapperDuration(samples);
        var boom = new InvalidOperationException("炸了");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => telemetry.MeasureAsync<int>("dapper", "update", "UPDATE T SET x = 1", async () =>
            {
                await Task.Yield();
                throw boom;
            }));

        Assert.Same(boom, thrown);
        Assert.Single(samples);
    }

    /// <summary>
    /// 助手真的在量，不是记个 0。样本耗时对不上就是表没掐对地方
    /// </summary>
    [Fact]
    public void Measure把委托里的等待真测进去()
    {
        var logger = new RecordingLogger();
        var telemetry = new QueryTelemetry(logger, 1);
        var samples = new List<(double Value, string Path, string Op)>();
        using var listener = ListenDapperDuration(samples);

        telemetry.Measure("dapper", "nonquery", "UPDATE Big SET x = 1", () =>
        {
            Thread.Sleep(60);
            return 1;
        });

        var sample = Assert.Single(samples);
        Assert.True(sample.Value >= 50, $"样本耗时只有 {sample.Value}ms，60ms 的等待没被量进去");

        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("path:dapper", warning);
        Assert.Contains("op:nonquery", warning);
    }

    [Fact]
    public void Measure把SQL透传到慢查询日志()
    {
        var logger = new RecordingLogger();
        var telemetry = new QueryTelemetry(logger, 1);

        telemetry.Measure("dapper", "nonquery", "UPDATE Big SET x = 1 WHERE id = @id", () =>
        {
            Thread.Sleep(10);
            return 1;
        });
        telemetry.Measure("dapper", "nonquery", null, () =>
        {
            Thread.Sleep(10);
            return 1;
        });

        Assert.Equal(2, logger.Warnings.Count);
        Assert.Contains("UPDATE Big SET x = 1 WHERE id = @id", logger.Warnings[0]);
        Assert.Contains("SQL:<none>", logger.Warnings[1]);
    }

    /// <summary>
    /// 收 viv.momo.query.duration 里 path=dapper 的样本。它是 Histogram&lt;double&gt;，
    /// query.slow 是 Counter&lt;long&gt;，拿 ListenLong 听会一条都收不到而且不报错。
    ///
    /// 滤掉 path=ef：那条是 DbCommandInterceptor 记的，别的测试类随时可能跑出样本。
    /// </summary>
    private static MeterListener ListenDapperDuration(List<(double Value, string Path, string Op)> samples)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == MomoMetrics.MeterName && instrument.Name == MomoMetrics.QueryDurationInstrument)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
        {
            var path = Tag(tags, "path");

            if (path != "dapper")
                return;

            samples.Add((value, path, Tag(tags, "op")));
        });
        listener.Start();
        return listener;
    }

    private static string Tag(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == key)
                return tag.Value?.ToString() ?? string.Empty;
        }

        return string.Empty;
    }

    /// <summary>
    /// 记一次并返回 viv.momo.query.slow 收到的量。监听器必须在 Record 之前起，
    /// 否则量测发生在没人听的窗口里
    /// </summary>
    private static long RecordAndCountSlow(QueryTelemetry telemetry, string path, string op, long elapsedMs, string? sql)
    {
        var seen = 0L;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == MomoMetrics.MeterName && instrument.Name == MomoMetrics.SlowQueryInstrument)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => seen += value);
        listener.Start();

        telemetry.Record(path, op, elapsedMs, sql);

        return seen;
    }
}
