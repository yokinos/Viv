using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;

namespace Viv.Engine.Tests;

/// <summary>
/// VivApiResult —— 业务信封 { Code, Message, Data }。
/// 白名单（VivRunDefine.AllowedHttpStatusCodes）内状态码原样保留，其余强制 200。
/// </summary>
public class VivApiResultTests
{
    private static async Task<int> ExecuteAsync(int preSetStatus)
    {
        var httpContext = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        httpContext.Response.StatusCode = preSetStatus;

        await new VivApiResult(0, "ok").ExecuteResultAsync(actionContext);

        return httpContext.Response.StatusCode;
    }

    [Fact]
    public async Task 白名单内404原样保留()
        => Assert.Equal(404, await ExecuteAsync(404));

    [Fact]
    public async Task 白名单内301重定向保留()
        => Assert.Equal(301, await ExecuteAsync(301));

    [Fact]
    public async Task 白名单内403保留()
        => Assert.Equal(403, await ExecuteAsync(403));

    [Fact]
    public async Task 白名单外500强制200()
        => Assert.Equal(200, await ExecuteAsync(500));

    [Fact]
    public async Task 白名单外400强制200()
        => Assert.Equal(200, await ExecuteAsync(400));

    [Fact]
    public async Task 默认200保持()
        => Assert.Equal(200, await ExecuteAsync(200));

    [Fact]
    public async Task 响应体为业务信封JSON()
    {
        var httpContext = new DefaultHttpContext();
        var body = new MemoryStream();
        httpContext.Response.Body = body;
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

        await VivApiResult.Failed("出错了").ExecuteResultAsync(actionContext);

        body.Position = 0;
        var json = new StreamReader(body).ReadToEnd();
        Assert.Contains("\"code\"", json);
        Assert.Contains("\"message\"", json);
        Assert.Contains("出错了", json);
        Assert.Contains("application/json", httpContext.Response.ContentType);
    }
}
