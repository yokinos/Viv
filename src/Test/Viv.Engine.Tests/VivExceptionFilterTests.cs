using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Viv.Contracts.Enums;
using Viv.Contracts.Exceptions;
using Viv.Engine.Filter;
using Viv.Log;

namespace Viv.Engine.Tests;

/// <summary>
/// 全局异常过滤器：VivConnectionException 不解包 Inner，按 ConnType 映射 -501/-502/-503。
/// </summary>
public class VivExceptionFilterTests
{
    [Fact]
    public async Task PostgreSQL连接异常_映射DatabaseError_不解包Inner()
    {
        var result = await Execute(new VivConnectionException(
            VivConnType.PostgreSQL, "insert failed", new InvalidOperationException("provider")));

        Assert.Equal((int)ApiResultCode.DatabaseError, result.Code);
        Assert.Contains("insert failed", result.Message);
    }

    [Fact]
    public async Task Redis连接异常_映射CacheError()
    {
        var result = await Execute(new VivConnectionException(VivConnType.Redis, "cache down"));
        Assert.Equal((int)ApiResultCode.CacheError, result.Code);
    }

    [Fact]
    public async Task RabbitMQ连接异常_映射MqError()
    {
        var result = await Execute(new VivConnectionException(VivConnType.RabbitMQ, "mq down"));
        Assert.Equal((int)ApiResultCode.MqError, result.Code);
    }

    [Fact]
    public async Task 未知异常_映射ServerError()
    {
        var result = await Execute(new InvalidOperationException("x"));
        Assert.Equal((int)ApiResultCode.ServerError, result.Code);
    }

    private static async Task<VivApiResult> Execute(Exception ex)
    {
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var exceptionContext = new ExceptionContext(actionContext, []) { Exception = ex };
        var filter = new VivExceptionFilterAttribute(new StubFilterLogger(), new StubHost());

        await filter.OnExceptionAsync(exceptionContext);

        Assert.True(exceptionContext.ExceptionHandled);
        return Assert.IsType<VivApiResult>(exceptionContext.Result);
    }

    private sealed class StubHost : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class StubFilterLogger : ILoggerContract
    {
        public void Info(string message, params object[] args) { }
        public void Debug(string message, params object[] args) { }
        public void Warning(string message, params object[] args) { }
        public void Error(string message, params object[] args) { }
        public void Error(string message, Exception ex, params object[] args) { }
        public void Fatal(string message, params object[] args) { }
        public void Fatal(string message, Exception ex, params object[] args) { }
    }
}
