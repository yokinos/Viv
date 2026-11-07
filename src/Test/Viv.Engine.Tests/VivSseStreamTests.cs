using System.Text;
using Microsoft.AspNetCore.Http;
using Viv.Engine.Sse;

namespace Viv.Engine.Tests;

/// <summary>
/// SSE 会话骨架的线格式与生命周期：id 自增、event 分类、终态唯一、心跳、取消传播、错误脱敏
/// </summary>
public class VivSseStreamTests
{
    private static (DefaultHttpContext Context, MemoryStream Body) CreateContext()
    {
        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;
        return (context, body);
    }

    private static VivSseOptions FastOptions(bool redact = true) => new()
    {
        RetryMilliseconds = null,
        HeartbeatInterval = TimeSpan.Zero,
        RedactErrorDetails = redact,
        ShutdownGracePeriod = TimeSpan.FromSeconds(1)
    };

    /// <summary>
    /// 解析线格式：空行分段，注释行（: 开头）与非 event/data/id 段（retry）跳过
    /// </summary>
    private static List<(string? Event, long? Id, string Data)> ParseFrames(string raw)
    {
        var frames = new List<(string?, long?, string)>();

        foreach (var block in raw.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            string? evt = null;
            long? id = null;
            var data = new List<string>();

            foreach (var line in block.Split('\n'))
            {
                if (line.Length == 0 || line[0] == ':')
                {
                    continue;
                }

                if (line.StartsWith("event: ", StringComparison.Ordinal))
                {
                    evt = line[7..];
                }
                else if (line.StartsWith("id: ", StringComparison.Ordinal))
                {
                    id = long.Parse(line[4..]);
                }
                else if (line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    data.Add(line[6..]);
                }
            }

            if (evt is null && id is null && data.Count == 0)
            {
                continue;
            }

            frames.Add((evt, id, string.Join("\n", data)));
        }

        return frames;
    }

    private static string ReadBody(MemoryStream body) => Encoding.UTF8.GetString(body.ToArray());

    [Fact]
    public async Task 增量帧带自增id与事件名_末尾自动补done帧()
    {
        var (context, body) = CreateContext();

        await context.Response.RunVivSseAsync(async (session, _) =>
        {
            await session.SendDeltaAsync("你");
            await session.SendDeltaAsync("好");
        }, traceId: "t-1", options: FastOptions());

        var raw = ReadBody(body);
        var frames = ParseFrames(raw);

        Assert.Equal("text/event-stream; charset=utf-8", context.Response.ContentType);
        Assert.Equal(new[] { "delta", "delta", "done" }, frames.Select(f => f.Event));
        Assert.Equal(new long[] { 1, 2, 3 }, frames.Select(f => f.Id!.Value));

        // 锁死线格式：id / event / data / 空行，且 data 里中文不被 \uXXXX 转义
        Assert.StartsWith(
            "id: 1\nevent: delta\ndata: {\"code\":0,\"message\":\"ok\",\"data\":{\"delta\":\"你\",\"seq\":1},\"traceId\":\"t-1\"}\n\n",
            raw);

        // data 层保持与旧信封一致的形状，delta 帧里的 seq 与线上 id 对得上
        Assert.Equal("{\"code\":0,\"message\":\"ok\",\"data\":{\"delta\":\"你\",\"seq\":1},\"traceId\":\"t-1\"}", frames[0].Data);
        Assert.Contains("\"message\":\"done\"", frames[2].Data);
    }

    [Fact]
    public async Task 结束帧带上汇总数据()
    {
        var (context, body) = CreateContext();

        await context.Response.RunVivSseAsync(async (session, _) =>
        {
            await session.SendDeltaAsync("a");
            await session.CompleteAsync(new { turnId = "x" });
        }, options: FastOptions());

        var frames = ParseFrames(ReadBody(body));

        Assert.Equal(2, frames.Count);
        Assert.Equal("done", frames[1].Event);
        Assert.Contains("\"turnId\":\"x\"", frames[1].Data);
    }

    [Fact]
    public async Task 业务异常转成error帧_生产环境不透出原文()
    {
        var (context, body) = CreateContext();

        await context.Response.RunVivSseAsync((_, _) => throw new InvalidOperationException("db password is hunter2"),
            traceId: "t-2", options: FastOptions(redact: true));

        var raw = ReadBody(body);
        var frames = ParseFrames(raw);

        Assert.Single(frames);
        Assert.Equal("error", frames[0].Event);
        Assert.DoesNotContain("hunter2", raw);
        Assert.Contains("\"traceId\":\"t-2\"", raw);
    }

    [Fact]
    public async Task 开发环境可以直接透出异常原文()
    {
        var (context, body) = CreateContext();

        await context.Response.RunVivSseAsync((_, _) => throw new InvalidOperationException("boom-detail"),
            options: FastOptions(redact: false));

        Assert.Contains("boom-detail", ReadBody(body));
    }

    [Fact]
    public async Task 自定义错误文案优先于脱敏开关()
    {
        var (context, body) = CreateContext();

        var options = FastOptions();
        options.ErrorMessageFactory = ex => $"E:{ex.GetType().Name}";

        await context.Response.RunVivSseAsync((_, _) => throw new ArgumentException("secret"),
            options: options);

        var raw = ReadBody(body);
        Assert.Contains("E:ArgumentException", raw);
        Assert.DoesNotContain("secret", raw);
    }

    [Fact]
    public async Task 可以推自定义事件名()
    {
        var (context, body) = CreateContext();

        await context.Response.RunVivSseAsync(async (session, _) =>
        {
            await session.SendAsync(new { name = "search" }, "tool_call");
        }, options: FastOptions());

        var frames = ParseFrames(ReadBody(body));

        Assert.Equal("tool_call", frames[0].Event);
        Assert.Contains("\"name\":\"search\"", frames[0].Data);
    }

    [Fact]
    public async Task 进入终态后再推事件返回false且不重复出帧()
    {
        var (context, body) = CreateContext();
        bool? afterTerminal = null;

        await context.Response.RunVivSseAsync(async (session, _) =>
        {
            await session.SendDeltaAsync("a");
            await session.CompleteAsync();
            afterTerminal = await session.SendDeltaAsync("b");
        }, options: FastOptions());

        var frames = ParseFrames(ReadBody(body));

        Assert.False(afterTerminal);
        Assert.Single(frames, f => f.Event == "done");
        Assert.DoesNotContain("\"delta\":\"b\"", ReadBody(body));
    }

    [Fact]
    public async Task 空闲时会补注释心跳_且不占用id()
    {
        var (context, body) = CreateContext();

        var options = FastOptions();
        options.HeartbeatInterval = TimeSpan.FromMilliseconds(50);

        await context.Response.RunVivSseAsync(async (session, ct) =>
        {
            await Task.Delay(400, ct);
            await session.SendDeltaAsync("late");
        }, options: options);

        var raw = ReadBody(body);
        var frames = ParseFrames(raw);

        Assert.Contains(": hb", raw);
        // 心跳是注释行，不参与 id 序列；业务帧仍然从 1 开始连续
        Assert.Equal(new long[] { 1, 2 }, frames.Select(f => f.Id!.Value));
    }

    [Fact]
    public async Task 客户端断开时把取消传给生产逻辑_且不再补done帧()
    {
        var (context, body) = CreateContext();
        using var cts = new CancellationTokenSource();
        var producerCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var options = FastOptions();
        options.HeartbeatInterval = TimeSpan.Zero;

        var run = context.Response.RunVivSseAsync(async (session, ct) =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
            catch (OperationCanceledException)
            {
                producerCancelled.TrySetResult();
                throw;
            }
        }, options: options, cancellationToken: cts.Token);

        await Task.Delay(100);
        await cts.CancelAsync();

        await run.WaitAsync(TimeSpan.FromSeconds(5));
        await producerCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.DoesNotContain("event: done", ReadBody(body));
    }

    [Fact]
    public async Task 请求已中止时直接返回_不写任何字节()
    {
        var (context, body) = CreateContext();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await context.Response.RunVivSseAsync(async (session, _) => { await session.SendDeltaAsync("x"); },
            options: FastOptions(), cancellationToken: cts.Token);

        Assert.Empty(ReadBody(body));
    }

    [Fact]
    public async Task 下发retry建议值()
    {
        var (context, body) = CreateContext();

        var options = FastOptions();
        options.RetryMilliseconds = 1500;

        await context.Response.RunVivSseAsync(async (session, _) => { await session.SendDeltaAsync("a"); },
            options: options);

        Assert.StartsWith("retry: 1500\n\n", ReadBody(body));
    }
}
