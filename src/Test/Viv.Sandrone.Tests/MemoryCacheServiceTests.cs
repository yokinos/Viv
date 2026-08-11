using Microsoft.Extensions.Caching.Memory;
using Viv.Sandrone.Impl;

namespace Viv.Sandrone.Tests;

public class MemoryCacheServiceTests
{
    private static MemoryCacheService CreateCache() => new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public void 未命中返回默认()
    {
        var cache = CreateCache();
        Assert.Equal(0, cache.Get<int>("missing"));
        Assert.Null(cache.Get<string>("missing"));
    }

    [Fact]
    public void Set与Get回环()
    {
        var cache = CreateCache();
        cache.Set("key", "value");
        Assert.Equal("value", cache.Get<string>("key"));
    }

    [Fact]
    public void TryGet命中与未命中()
    {
        var cache = CreateCache();
        cache.Set("key", 42);

        Assert.True(cache.TryGet("key", out int value));
        Assert.Equal(42, value);
        Assert.False(cache.TryGet("missing", out int _));
    }

    [Fact]
    public void Remove清除()
    {
        var cache = CreateCache();
        cache.Set("key", "value");
        cache.Remove("key");
        Assert.Null(cache.Get<string>("key"));
    }

    [Fact]
    public void GetOrAdd工厂只调一次()
    {
        var cache = CreateCache();
        int calls = 0;

        var a = cache.GetOrAdd("key", () => { calls++; return "v"; });
        var b = cache.GetOrAdd("key", () => { calls++; return "v"; });

        Assert.Equal("v", a);
        Assert.Equal("v", b);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void 绝对过期后失效()
    {
        var cache = CreateCache();
        cache.Set("key", "value", TimeSpan.FromMilliseconds(30));

        Thread.Sleep(100);
        Assert.Null(cache.Get<string>("key"));
    }

    [Fact]
    public async Task GetOrAddAsync回环()
    {
        var cache = CreateCache();
        var value = await cache.GetOrAddAsync("key", _ => ValueTask.FromResult(123));
        Assert.Equal(123, value);
        Assert.Equal(123, cache.Get<int>("key"));
    }
}
