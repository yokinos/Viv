using Microsoft.AspNetCore.Http;
using Viv.Contracts;
using Viv.Engine.Middleware;
using Viv.Engine.Power;

namespace Viv.Engine.Tests;

/// <summary>
/// RequestTrackMiddleware —— holderId 只在验签通过后采用上游值，否则本进程生成。
/// </summary>
[Collection("VivEngineStaticState")]
public class RequestTrackMiddlewareTests
{
    private const string Secret = "test-secret-0123456789abcdef0123456789abcdef";

    public RequestTrackMiddlewareTests()
    {
        EngineTestEnv.ForceFallbackMode();
        LockHolderContext.Clear();
    }

    [Fact]
    public async Task 验签通过且带holderId_采用上游值()
    {
        EngineTestEnv.ForceEnvTokenMode(Secret);
        var headers = SignedHeaders("upstream-holder");
        headers[VivRunDefine.VivTraceIdHeader] = "trace-1";
        string? seen = null;

        await InvokeAsync(headers, () => seen = LockHolderContext.CurrentHolderId);

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

        await InvokeAsync(headers, () => seen = LockHolderContext.CurrentHolderId);

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

        await InvokeAsync(headers, () => seen = LockHolderContext.CurrentHolderId);

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

        await InvokeAsync(headers, () => seen = LockHolderContext.CurrentHolderId);

        Assert.False(string.IsNullOrWhiteSpace(seen));
        Assert.NotEqual("captured-holder", seen);
    }

    private static async Task InvokeAsync(IHeaderDictionary headers, Action onInvoke)
    {
        var http = new DefaultHttpContext();
        foreach (var (key, value) in headers)
        {
            http.Request.Headers[key] = value;
        }

        var mw = new RequestTrackMiddleware(_ =>
        {
            onInvoke();
            return Task.CompletedTask;
        });

        await mw.InvokeAsync(http);
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
