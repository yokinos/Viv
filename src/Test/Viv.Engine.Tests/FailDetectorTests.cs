using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Viv.Engine.LocalEvents;

namespace Viv.Engine.Tests;

/// <summary>
/// 「这次调用算不算失败」的判定 —— 事务回滚与本地事件分发共用同一条规则。
///
/// 信封那段现在只有一份实现（<see cref="FailDetector"/>），本地事件过滤器直接调它，
/// 所以这里不再需要盯着两份副本不漂移；但仍然从过滤器那一侧再验一遍，
/// 因为过滤器还叠了「异常算不算失败」的判断，那部分只有它自己有。
/// </summary>
public class FailDetectorTests
{
    /// <summary>两边都要给出相同答案的输入</summary>
    public static TheoryData<object?, bool> Cases() => new()
    {
        { null, false },
        { VivApiResult.Success("ok"), false },
        { VivApiResult.ApiResult(ApiResultCode.Accepted), false },   // 201 —— 2xx 区间
        { new VivApiResult(299, "上边界"), false },
        { new VivApiResult(300, "刚出界"), true },
        { new VivApiResult(199, "刚出界"), true },
        { VivApiResult.Failed("业务失败"), true },                    // -200
        { VivApiResult.ApiResult(ApiResultCode.ServerError), true },
        { VivApiResult.ApiResult(ApiResultCode.NotFound), true },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void 信封成败_2xx区间算成功(object? result, bool expected)
        => Assert.Equal(expected, FailDetector.IsFailed(result));

    [Theory]
    [MemberData(nameof(Cases))]
    public void 本地事件侧_信封判定与事务侧一致(object? result, bool expected)
        => Assert.Equal(expected, InvokeFlushFilterIsFailed(result));

    /// <summary>
    /// 非信封返回值与未拆包的 Task 一律视为成功。
    ///
    /// 未拆包的 <c>Task</c> 绝不能去 <c>.Result</c> 阻塞等待 —— 真正拆包由
    /// <c>AsyncInterceptorBase</c> 的泛型重载完成，走到这里时 T 已经是拆好的返回值了。
    /// </summary>
    [Fact]
    public void 非信封返回值与未拆包的Task_一律视为成功()
    {
        Assert.False(FailDetector.IsFailed(Task.CompletedTask));
        Assert.False(FailDetector.IsFailed("不是信封"));
        Assert.False(FailDetector.IsFailed(new object()));
    }

    /// <summary>
    /// 过滤器独有的那部分：异常无论有没有被接住都算失败。
    ///
    /// 信封判定已复用 <see cref="FailDetector"/>，但「异常 → 失败」这条只有过滤器能看见
    /// （拦截器那边异常是直接抛出来的，走的是 catch 分支）。
    /// </summary>
    [Fact]
    public void 本地事件侧_异常一律判失败_不管有没有被接住()
    {
        // 异常已被异常过滤器接住 → MVC 会剥离 Exception，只剩 ExceptionHandled 这个信号
        Assert.True(InvokeFlushFilterIsFailed(null, exceptionHandled: true));

        // 未接住的异常不会被判到这里（next() 已经抛出），兜底判一次以防过滤链行为变化
        Assert.True(InvokeFlushFilterIsFailed(null, exception: new InvalidOperationException("炸了")));

        // 对照：既没异常也没失败信封 → 成功
        Assert.False(InvokeFlushFilterIsFailed(null));
    }

    /// <summary>
    /// 直接调 <c>LocalEventFlushFilterAttribute.IsFailed</c>（private static）——
    /// 验的是过滤器对外呈现的行为，走反射是为了不为测试放宽它的可见性。
    /// </summary>
    private static bool InvokeFlushFilterIsFailed(
        object? result, bool exceptionHandled = false, Exception? exception = null)
    {
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        var executed = new ActionExecutedContext(actionContext, [], new object())
        {
            Result = result as IActionResult,
            ExceptionHandled = exceptionHandled,
            Exception = exception
        };

        var method = typeof(LocalEventFlushFilterAttribute)
            .GetMethod("IsFailed", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("LocalEventFlushFilterAttribute.IsFailed 不见了 —— 约定被改动");

        return (bool)method.Invoke(null, [executed])!;
    }
}
