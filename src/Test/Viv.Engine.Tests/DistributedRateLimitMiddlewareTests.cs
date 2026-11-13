using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Viv.Engine.Options;
using Viv.Engine.RateLimit;

namespace Viv.Engine.Tests;

/// <summary>
/// 分布式限流的判定路径。这里不碰真 Redis —— 要验的是「超限怎么判、计数器不可用时怎么降级」，
/// 计数本身是 Redis 的事（INCR 的原子性由服务端保证）。
/// </summary>
public class DistributedRateLimitMiddlewareTests
{
    [Fact]
    public async Task 计数未超限时放行()
    {
        var counter = new ScriptedCounter(1, 2);
        var (invoke, context) = Build(permitLimit: 2, counter);

        await invoke(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(1, context.Items["next"]);
        Assert.Single(counter.Keys);
    }

    [Fact]
    public async Task 计数超限时返回429且不转发()
    {
        var counter = new ScriptedCounter(3);
        var (invoke, context) = Build(permitLimit: 2, counter);

        await invoke(context);

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.False(context.Items.ContainsKey("next"));
        // 客户端要知道多久之后再来，否则只能瞎重试
        Assert.False(string.IsNullOrEmpty(context.Response.Headers.RetryAfter));
    }

    [Fact]
    public async Task 窗口键里带客户端标识与时间片()
    {
        var counter = new ScriptedCounter(1);
        var (invoke, context) = Build(permitLimit: 5, counter);

        await invoke(context);

        var key = Assert.Single(counter.Keys);
        Assert.StartsWith("viv:rl:10.1.2.3:", key);
        Assert.True(counter.LastTtl > TimeSpan.Zero);
    }

    [Fact]
    public async Task 计数器不可用时退回本机限流而不是全放行()
    {
        // 计数器永远不可用；配额 1 次，所以第二次必须被拦
        var counter = new ScriptedCounter((long?)null);
        var (invoke, contextOne) = Build(permitLimit: 1, counter);
        var contextTwo = NewContext();

        await invoke(contextOne);
        Assert.Equal(StatusCodes.Status200OK, contextOne.Response.StatusCode);

        await invoke(contextTwo);
        Assert.Equal(StatusCodes.Status429TooManyRequests, contextTwo.Response.StatusCode);
    }

    [Fact]
    public async Task 配额为零表示不限流_不碰计数器()
    {
        var counter = new ScriptedCounter(999);
        var (invoke, context) = Build(permitLimit: 0, counter);

        await invoke(context);

        Assert.Equal(1, context.Items["next"]);
        Assert.Empty(counter.Keys);
    }

    private static (Func<HttpContext, Task> Invoke, HttpContext Context) Build(int permitLimit, IRateLimitCounter counter)
    {
        var context = NewContext();
        var options = new StaticOptionsMonitor<VivRateLimitOptions>(new VivRateLimitOptions
        {
            PermitLimit = permitLimit,
            Window = 10,
            // 兜底路径上不排队：排队会让「第二次必被拦」的断言变成等 10 秒
            QueueLimit = 0
        });

        var middleware = new DistributedRateLimitMiddleware(
            next: ctx =>
            {
                ctx.Items["next"] = (ctx.Items.TryGetValue("next", out var n) ? (int)n! : 0) + 1;
                return Task.CompletedTask;
            },
            options,
            counter,
            context.RequestServices);

        return (middleware.InvokeAsync, context);
    }

    private static HttpContext NewContext()
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider()
        };

        context.Connection.RemoteIpAddress = IPAddress.Parse("10.1.2.3");
        context.Response.Body = new MemoryStream();
        return context;
    }

    /// <summary>按顺序吐预设值；值用完后一直重复最后一个（<c>null</c> 可以模拟「Redis 一直不可用」）</summary>
    private sealed class ScriptedCounter : IRateLimitCounter
    {
        private readonly Queue<long?> _values;

        public ScriptedCounter(params long?[] values)
        {
            // 单个 null 实参会被 C# 当成「数组本身是 null」，这里兜一下
            _values = new Queue<long?>(values ?? [null]);
        }

        public List<string> Keys { get; } = [];

        public TimeSpan LastTtl { get; private set; }

        public Task<long?> TryIncrementAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            Keys.Add(key);
            LastTtl = ttl;

            var value = _values.Count > 1 ? _values.Dequeue() : _values.Peek();
            return Task.FromResult(value);
        }
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T value)
        {
            CurrentValue = value;
        }

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
