using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Viv.Redis;

namespace Viv.Redis.Tests;

/// <summary>
/// Redis 指标。仪器名是面板上的契约（改一次所有图表跟着断），所以逐字钉住；
/// Record* 是埋点的入口，用 MeterListener 确认记了就出得来、标签也对。
///
/// 覆盖不到的是「接没接上」—— 四个 ExecuteRedis* 重载、连接事件回调、锁续期那几处真的调没调
/// Record*，只有连真 Redis 才验得了，仓库没有那条测试基座。
/// </summary>
public class RedisMetricsTests
{
    [Fact]
    public void 仪器名稳定()
    {
        Assert.Equal("Viv.Redis", RedisMetrics.MeterName);

        Assert.Equal("viv.redis.command.duration", RedisMetrics.CommandDurationInstrument);
        Assert.Equal("viv.redis.errors", RedisMetrics.ErrorsInstrument);
        Assert.Equal("viv.redis.connection.failed", RedisMetrics.ConnectionFailedInstrument);
        Assert.Equal("viv.redis.connection.restored", RedisMetrics.ConnectionRestoredInstrument);
        Assert.Equal("viv.redis.cache.result", RedisMetrics.CacheResultInstrument);
        Assert.Equal("viv.redis.fallback", RedisMetrics.FallbackInstrument);
        Assert.Equal("viv.redis.lock.acquire.duration", RedisMetrics.LockAcquireDurationInstrument);
        Assert.Equal("viv.redis.lock.renewal.stopped", RedisMetrics.LockRenewalStoppedInstrument);
        Assert.Equal("viv.redis.lock.release.failed", RedisMetrics.LockReleaseFailedInstrument);
    }

    [Fact]
    public void RecordCache带hit与miss两个取值()
    {
        var results = new List<string>();
        using var listener = ListenLong(RedisMetrics.CacheResultInstrument, (_, tags) => results.Add(Tag(tags, "result")));

        RedisMetrics.RecordCache(true);
        RedisMetrics.RecordCache(false);

        Assert.Contains("hit", results);
        Assert.Contains("miss", results);
    }

    [Fact]
    public void RecordCommand失败时errors加一()
    {
        var seen = 0L;
        using var listener = ListenLong(RedisMetrics.ErrorsInstrument, (value, _) => seen += value);

        RedisMetrics.RecordCommand(12, true);
        Assert.Equal(0, seen);

        RedisMetrics.RecordCommand(34, false);
        Assert.Equal(1, seen);
    }

    [Fact]
    public void RecordFallback带reason标签()
    {
        var reasons = new List<string>();
        using var listener = ListenLong(RedisMetrics.FallbackInstrument, (_, tags) => reasons.Add(Tag(tags, "reason")));

        RedisMetrics.RecordFallback("cache");
        RedisMetrics.RecordFallback("lock");

        Assert.Contains("cache", reasons);
        Assert.Contains("lock", reasons);
    }

    [Fact]
    public void RecordLockAcquire是耗时直方图_带acquired与timeout()
    {
        var results = new List<string>();
        using var listener = ListenDouble(RedisMetrics.LockAcquireDurationInstrument, (_, tags) => results.Add(Tag(tags, "result")));

        RedisMetrics.RecordLockAcquire(5, true);
        RedisMetrics.RecordLockAcquire(9, false);

        Assert.Contains("acquired", results);
        Assert.Contains("timeout", results);
    }

    [Fact]
    public void 三个无标签计数各自独立()
    {
        var seen = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == RedisMetrics.MeterName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            for (var i = 0; i < value; i++)
                seen.Add(instrument.Name);
        });
        listener.Start();

        RedisMetrics.RecordLockRenewalStopped();
        RedisMetrics.RecordLockReleaseFailed();

        Assert.Contains(RedisMetrics.LockRenewalStoppedInstrument, seen);
        Assert.Contains(RedisMetrics.LockReleaseFailedInstrument, seen);
    }

    private static MeterListener ListenLong(string instrumentName, Action<long, List<KeyValuePair<string, object?>>> onMeasurement)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == RedisMetrics.MeterName && instrument.Name == instrumentName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) => onMeasurement(value, tags.ToArray().ToList()));
        listener.Start();
        return listener;
    }

    private static MeterListener ListenDouble(string instrumentName, Action<double, List<KeyValuePair<string, object?>>> onMeasurement)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == RedisMetrics.MeterName && instrument.Name == instrumentName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) => onMeasurement(value, tags.ToArray().ToList()));
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
