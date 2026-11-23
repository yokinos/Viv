using Grpc.Core;
using Viv.Keke.Grpc;
using Viv.Keke.Identity;

namespace Viv.Keke.Tests;

/// <summary>
/// gRPC 客户端拦截器的写头逻辑 —— 与 MagicOnion 客户端 Filter 相反，这里是
/// <b>缺了才加</b>：<see cref="Metadata"/> 由调用方每次构造，已有值说明调用方显式设过，应当尊重。
/// </summary>
public class VivGrpcInterceptorTests
{
    [Fact]
    public void 写入全部身份头()
    {
        var headers = new Metadata();

        VivGrpcInterceptor.AddVivHeaders(headers, Values());

        Assert.Equal("11", headers.Get("x-viv-appid")?.Value);
        Assert.Equal("22", headers.Get("x-viv-subjectid")?.Value);
        Assert.Equal("33", headers.Get("x-viv-userid")?.Value);
        Assert.Equal("viv.apex.api", headers.Get("x-viv-servicename")?.Value);
        Assert.Equal("holder-1", headers.Get("x-viv-holder-id")?.Value);
        Assert.Equal("trace-1", headers.Get("x-trace-id")?.Value);
        Assert.Equal("1:sig", headers.Get("x-request-token")?.Value);
    }

    [Fact]
    public void traceId为空时整条不带()
    {
        var headers = new Metadata();

        VivGrpcInterceptor.AddVivHeaders(headers, Values(traceId: null));

        // 后台任务调 gRPC 时没有 traceId，不带这条比带空串好：
        // 下游 RequestTrackMiddleware 只看「有没有值」，空串会让它误判成上游给了个空值
        Assert.Null(headers.Get("x-trace-id"));
    }

    [Fact]
    public void traceId有值时带上()
    {
        var headers = new Metadata();

        VivGrpcInterceptor.AddVivHeaders(headers, Values(traceId: "trace-abc"));

        Assert.Equal("trace-abc", headers.Get("x-trace-id")?.Value);
    }

    [Fact]
    public void 已存在的头不被覆盖()
    {
        var headers = new Metadata
        {
            { "x-viv-holder-id", "caller-supplied" },
            { "x-trace-id", "caller-trace" },
        };

        VivGrpcInterceptor.AddVivHeaders(headers, Values());

        // 与 MagicOnion 侧语义相反：gRPC 的 Metadata 由调用方构造，显式设过的值要尊重
        Assert.Equal("caller-supplied", headers.Get("x-viv-holder-id")?.Value);
        Assert.Equal("caller-trace", headers.Get("x-trace-id")?.Value);
        Assert.Single(headers.Where(e => e.Key == "x-viv-holder-id"));
    }

    [Fact]
    public void 未签出token时留空让下游按未认证处理()
    {
        var headers = new Metadata();

        VivGrpcInterceptor.AddVivHeaders(headers, Values(requestToken: null));

        Assert.Null(headers.Get("x-request-token"));
    }

    private static VivIdentityValues Values(string? traceId = "trace-1", string? requestToken = "1:sig")
        => new("11", "22", "33", "viv.apex.api", "holder-1", traceId, requestToken);
}
