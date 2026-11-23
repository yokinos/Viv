using Microsoft.AspNetCore.Http;
using Viv.Contracts;
using Viv.Engine.Middleware;
using Viv.Engine.Power;

namespace Viv.Engine.Tests;

/// <summary>
/// RequestTrackMiddleware —— traceId 一律服务端生成（网关丢弃客户端值并写回请求头，下游复用）；
/// holderId 只在验签通过后采用上游值，否则本进程生成。
/// </summary>
[Collection("VivEngineStaticState")]
public class RequestTrackMiddlewareTests
{
    private const string Secret = "test-secret-0123456789abcdef0123456789abcdef";

    public RequestTrackMiddlewareTests()
    {
        EngineTestEnv.ForceFallbackMode();
        VivCallContext.Clear();
    }

    [Fact]
    public async Task 验签通过且带holderId_采用上游值()
    {
        EngineTestEnv.ForceEnvTokenMode(Secret);
        var headers = SignedHeaders("upstream-holder");
        headers[VivRunDefine.VivTraceIdHeader] = "trace-1";
        string? seen = null;

        await InvokeAsync(headers, () => seen = VivCallContext.HolderId);

        Assert.Equal("upstream-holder", seen);
    }

    [Fact]
    public async Task 无密钥_忽略客户端holderId头()
    {
        var headers = new HeaderDictionary
        {
            [VivRunDefine.AppIdHeader] = "1",
            [VivRunDefine.UserIdHeader] = "2",
            [VivRunDefine.HolderIdHeader] = "forged-holder",
            [VivRunDefine.VivTraceIdHeader] = "trace-1",
        };
        string? seen = null;

        await InvokeAsync(headers, () => seen = VivCallContext.HolderId);

        Assert.False(string.IsNullOrWhiteSpace(seen));
        Assert.NotEqual("forged-holder", seen);
    }

    [Fact]
    public async Task 篡改holderId_不采用伪造值()
    {
        EngineTestEnv.ForceEnvTokenMode(Secret);
        var headers = SignedHeaders("real-holder");
        headers[VivRunDefine.HolderIdHeader] = "forged-holder";
        headers[VivRunDefine.VivTraceIdHeader] = "trace-1";
        string? seen = null;

        await InvokeAsync(headers, () => seen = VivCallContext.HolderId);

        Assert.False(string.IsNullOrWhiteSpace(seen));
        Assert.NotEqual("forged-holder", seen);
        Assert.NotEqual("real-holder", seen);
    }

    [Fact]
    public async Task 网关_即使验签通过也不采用入站holderId()
    {
        EngineTestEnv.ForceEnvTokenMode(Secret, serviceType: 2);
        var headers = SignedHeaders("captured-holder");
        string? seen = null;

        await InvokeAsync(headers, () => seen = VivCallContext.HolderId);

        Assert.False(string.IsNullOrWhiteSpace(seen));
        Assert.NotEqual("captured-holder", seen);
    }

    [Fact]
    public async Task 网关_丢弃客户端traceId并写进转发请求头()
    {
        EngineTestEnv.ForceEnvTokenMode(Secret, serviceType: 2);
        var headers = new HeaderDictionary { [VivRunDefine.VivTraceIdHeader] = "client-supplied" };

        var http = await InvokeAsync(headers);

        var traceId = Assert.IsType<string>(http.Items[VivRunDefine.ContextTraceId]);
        Assert.False(string.IsNullOrWhiteSpace(traceId));
        Assert.NotEqual("client-supplied", traceId);

        // 写请求头才是关键：不写的话 YARP 转出去那一跳没有它，下游又会自己造一条
        Assert.Equal(traceId, http.Request.Headers[VivRunDefine.VivTraceIdHeader].ToString());
        Assert.Equal(traceId, http.Response.Headers[VivRunDefine.VivTraceIdHeader].ToString());
    }

    [Fact]
    public async Task 下游_复用网关写进来的traceId()
    {
        var headers = new HeaderDictionary { [VivRunDefine.VivTraceIdHeader] = "upstream-trace" };

        var http = await InvokeAsync(headers);

        Assert.Equal("upstream-trace", http.Items[VivRunDefine.ContextTraceId]);
        Assert.Equal("upstream-trace", http.Response.Headers[VivRunDefine.VivTraceIdHeader].ToString());
    }

    [Fact]
    public async Task 下游_没有上游traceId时自己生成()
    {
        var http = await InvokeAsync(new HeaderDictionary());

        var traceId = Assert.IsType<string>(http.Items[VivRunDefine.ContextTraceId]);
        Assert.False(string.IsNullOrWhiteSpace(traceId));
    }

    [Fact]
    public async Task traceId写进VivCallContext且请求结束后清理()
    {
        string? seenInPipeline = null;

        await InvokeAsync(new HeaderDictionary(), () => seenInPipeline = VivCallContext.TraceId);

        // gRPC / MagicOnion 客户端拦截器靠这个静态访问器把 traceId 带进下游 metadata
        Assert.False(string.IsNullOrWhiteSpace(seenInPipeline));

        // 必须清掉：AsyncLocal 在请求结束后若留着值，线程池上的下一个请求会继承上一个的 traceId
        Assert.Null(VivCallContext.TraceId);
    }

    private static async Task<DefaultHttpContext> InvokeAsync(IHeaderDictionary headers, Action? onInvoke = null)
    {
        var http = new DefaultHttpContext();
        foreach (var (key, value) in headers)
        {
            http.Request.Headers[key] = value;
        }

        var mw = new RequestTrackMiddleware(_ =>
        {
            onInvoke?.Invoke();
            return Task.CompletedTask;
        });

        await mw.InvokeAsync(http);
        return http;
    }

    private static HeaderDictionary SignedHeaders(string holderId)
    {
        var headers = new HeaderDictionary
        {
            [VivRunDefine.AppIdHeader] = "1",
            [VivRunDefine.SubjectIdHeader] = "3",
            [VivRunDefine.UserIdHeader] = "2",
            [VivRunDefine.ServiceNameHeader] = "viv.apex.api",
            [VivRunDefine.HolderIdHeader] = holderId,
            [VivRunDefine.VivTraceIdHeader] = "trace-1",
        };
        headers[VivRunDefine.InnerRequestTokenHeader] = RequestTokenResolver.SignContextHeaders(headers);
        return headers;
    }
}
