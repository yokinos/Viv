using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Viv.Contracts.Enums;
using Viv.Contracts.Exceptions;
using Viv.Delusion.Extension;
using Viv.Engine.Filter;
using Viv.Fakes;

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
        Assert.Equal(ApiResultCode.DatabaseError.GetDescription(), result.Message);
        Assert.DoesNotContain("insert failed", result.Message);
        Assert.DoesNotContain("provider", result.Message);
    }

    [Fact]
    public async Task Redis连接异常_映射CacheError()
    {
        var result = await Execute(new VivConnectionException(VivConnType.Redis, "cache down"));
        Assert.Equal((int)ApiResultCode.CacheError, result.Code);
        Assert.Equal(ApiResultCode.CacheError.GetDescription(), result.Message);
        Assert.DoesNotContain("cache down", result.Message);
    }

    [Fact]
    public async Task RabbitMQ连接异常_映射MqError()
    {
        var result = await Execute(new VivConnectionException(VivConnType.RabbitMQ, "mq down"));
        Assert.Equal((int)ApiResultCode.MqError, result.Code);
        Assert.Equal(ApiResultCode.MqError.GetDescription(), result.Message);
        Assert.DoesNotContain("mq down", result.Message);
    }

    [Fact]
    public async Task 未知异常_映射ServerError()
    {
        var result = await Execute(new InvalidOperationException("x"));
        Assert.Equal((int)ApiResultCode.ServerError, result.Code);
    }

    /// <summary>
    /// 锁异常的 Message 里带着 LockKey（租户 + 用户 + 业务主键），生产环境不能回给客户端。
    /// 错误码仍是 BusyError，客户端照旧可以重试，只是看不到内部 Key。
    /// </summary>
    [Fact]
    public async Task 分布式锁异常_生产环境不回LockKey()
    {
        var result = await Execute(
            new DistributedLockException("lock:tenant:7:user:42:order:99", retryCount: 3),
            Environments.Production);

        Assert.Equal((int)ApiResultCode.BusyError, result.Code);
        Assert.Equal("操作繁忙，请稍后重试", result.Message);
        Assert.DoesNotContain("lock:", result.Message);
        Assert.DoesNotContain("42", result.Message);
    }

    /// <summary>开发环境保留原文便于排查，与未映射异常的堆栈口径一致。</summary>
    [Fact]
    public async Task 分布式锁异常_开发环境保留原文()
    {
        var result = await Execute(
            new DistributedLockException("lock:tenant:7:user:42:order:99", retryCount: 3),
            Environments.Development);

        Assert.Equal((int)ApiResultCode.BusyError, result.Code);
        Assert.Contains("lock:tenant:7", result.Message);
    }

    /// <summary>业务异常的消息本来就是写给客户端的，生产环境不能被一起抹掉。</summary>
    [Fact]
    public async Task 业务异常_生产环境保留业务文案()
    {
        var result = await Execute(new VivBusinessException("余额不足"), Environments.Production);

        Assert.Equal("余额不足", result.Message);
    }

    private static Task<VivApiResult> Execute(Exception ex)
        => Execute(ex, Environments.Development);

    private static async Task<VivApiResult> Execute(Exception ex, string environmentName)
    {
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var exceptionContext = new ExceptionContext(actionContext, []) { Exception = ex };
        var filter = new VivExceptionFilterAttribute(
            new RecordingLogger(),
            new StubHost { EnvironmentName = environmentName });

        await filter.OnExceptionAsync(exceptionContext);

        Assert.True(exceptionContext.ExceptionHandled);
        return Assert.IsType<VivApiResult>(exceptionContext.Result);
    }
}
