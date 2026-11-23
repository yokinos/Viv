using Viv.Contracts;

namespace Viv.Redis.Tests;

/// <summary>
/// 调用上下文 —— AsyncLocal 承载 traceId 与 holderId，同一异步流内复用、可显式设置/重置/清除。
/// holderId 是"谁持有这把锁"的身份，被 RedisService 的 Lua 脚本拿去比对防误删；
/// traceId 只用于日志串联，两者共用一个槽位集合后，清理语义必须分得清。
/// </summary>
public class VivCallContextTests
{
    [Fact]
    public void 首次访问_自动生成非空Id()
    {
        var id = VivCallContext.HolderId;
        Assert.False(string.IsNullOrWhiteSpace(id));
    }

    [Fact]
    public void 同一异步流_复用同一Id()
    {
        var first = VivCallContext.HolderId;
        var second = VivCallContext.HolderId;
        Assert.Equal(first, second);
    }

    [Fact]
    public void 显式设置_覆盖Id()
    {
        VivCallContext.SetHolderId("test-holder");
        Assert.Equal("test-holder", VivCallContext.HolderId);
    }

    [Fact]
    public void 重置_生成新Id()
    {
        var before = VivCallContext.HolderId;
        VivCallContext.ResetHolderId();
        var after = VivCallContext.HolderId;
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void 清除_再访问重新生成()
    {
        var before = VivCallContext.HolderId;
        VivCallContext.Clear();
        var after = VivCallContext.HolderId;
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void traceId_设置后同一异步流内可见()
    {
        VivCallContext.Clear();

        VivCallContext.SetTraceId("trace-abc");

        Assert.Equal("trace-abc", VivCallContext.TraceId);
    }

    [Fact]
    public void Clear_连traceId一起清()
    {
        VivCallContext.SetTraceId("trace-abc");
        var beforeHolder = VivCallContext.HolderId;

        VivCallContext.Clear();

        Assert.Null(VivCallContext.TraceId);
        // holderId 是懒生成的：清完再读会拿到一个新的，而不是 null
        Assert.NotEqual(beforeHolder, VivCallContext.HolderId);
    }

    [Fact]
    public void ClearHolderId_保留traceId()
    {
        VivCallContext.SetTraceId("trace-abc");
        VivCallContext.SetHolderId("holder-1");

        VivCallContext.ClearHolderId();

        // 消息消费者只该清 holder：本地事件跑在同一请求的异步流里，traceId 清掉就丢链路了
        Assert.Equal("trace-abc", VivCallContext.TraceId);
        Assert.NotEqual("holder-1", VivCallContext.HolderId);
    }
}
