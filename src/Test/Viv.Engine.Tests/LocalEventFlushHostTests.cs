using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Viv.Contracts.Events;
using Viv.Contracts.Interface;
using Viv.Engine.LocalEvents;
using Viv.Fakes;
using Viv.Log;

namespace Viv.Engine.Tests;

/// <summary>
/// 本地事件 HTTP 触发点：过滤器改写信封、中间件按信封 Discard、作用域 RunAsync。
/// </summary>
public class LocalEventFlushHostTests
{
    public LocalEventFlushHostTests() => Hits.Clear();

    private static readonly List<string> Hits = [];

    public sealed class HostEvent : LocalEvent;

    public sealed class HostHandler : IVivLocalEventHandler<HostEvent>
    {
        public Task HandleAsync(HostEvent @event, CancellationToken ct = default)
        {
            Hits.Add("host");
            return Task.CompletedTask;
        }
    }

    public sealed class ThrowingHostHandler : IVivLocalEventHandler<HostEvent>
    {
        public Task HandleAsync(HostEvent @event, CancellationToken ct = default)
            => throw new InvalidOperationException("handler炸了");
    }

    private static LocalEventBus RealBus(params IVivLocalEventHandler<HostEvent>[] handlers)
        => new([new LocalEventHandlerInvoker<HostEvent>(handlers)], new RecordingLogger());

    private static ActionExecutingContext Executing(HttpContext? http = null)
    {
        var actionContext = new ActionContext(http ?? new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        return new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), new object());
    }

    [Fact]
    public async Task 过滤器_成功Flush_handler在提交之后的位置执行()
    {
        var bus = RealBus(new HostHandler());
        var filter = new LocalEventFlushFilterAttribute(bus, new RecordingLogger());

        await filter.OnActionExecutionAsync(Executing(), async () =>
        {
            await bus.PublishAsync(new HostEvent());
            var ctx = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
            return new ActionExecutedContext(ctx, [], new object())
            {
                Result = VivApiResult.Success("ok")
            };
        });

        Assert.Equal("host", Assert.Single(Hits));
    }

    [Fact]
    public async Task 过滤器_失败信封_Discard()
    {
        var bus = RealBus(new HostHandler());
        var filter = new LocalEventFlushFilterAttribute(bus, new RecordingLogger());

        await filter.OnActionExecutionAsync(Executing(), async () =>
        {
            await bus.PublishAsync(new HostEvent());
            var ctx = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
            return new ActionExecutedContext(ctx, [], new object())
            {
                Result = VivApiResult.Failed("业务失败")
            };
        });

        Assert.Empty(Hits);
    }

    [Fact]
    public async Task 过滤器_next抛异常_Discard再上抛()
    {
        var bus = new RecordingLocalEventBus();
        var filter = new LocalEventFlushFilterAttribute(bus, new RecordingLogger());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => filter.OnActionExecutionAsync(Executing(), () => throw new InvalidOperationException("action炸了")));

        Assert.Equal(0, bus.FlushCalls);
        Assert.Equal(1, bus.DiscardCalls);
    }

    [Fact]
    public async Task 过滤器_Flush失败_改成错误信封并记主业务已提交()
    {
        var logger = new RecordingLogger();
        var bus = new RecordingLocalEventBus { FlushException = new InvalidOperationException("handler炸了") };
        var filter = new LocalEventFlushFilterAttribute(bus, logger);
        ActionExecutedContext? executed = null;

        await filter.OnActionExecutionAsync(Executing(), () =>
        {
            executed = new ActionExecutedContext(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
                [],
                new object())
            {
                Result = VivApiResult.Success("ok")
            };
            return Task.FromResult(executed);
        });

        var envelope = Assert.IsType<VivApiResult>(executed!.Result);
        Assert.Equal((int)ApiResultCode.ServerError, envelope.Code);
        Assert.Contains("主业务已提交", envelope.Message);
        Assert.Contains(logger.Errors, e => e.Contains("主业务已提交"));
        Assert.Equal(1, bus.DiscardCalls);
    }

    [Fact]
    public async Task 中间件_HTTP200但信封失败_Discard()
    {
        var bus = new RecordingLocalEventBus();
        var http = new DefaultHttpContext { Response = { StatusCode = 200 } };
        http.Items[VivRunDefine.ApiResultItemKey] = VivApiResult.Failed("业务失败");

        var middleware = new LocalEventFlushMiddleware(_ =>
        {
            bus.Published.Add(new HostEvent());
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(http, bus, new RecordingLogger());

        Assert.Equal(0, bus.FlushCalls);
        Assert.Equal(1, bus.DiscardCalls);
    }

    [Fact]
    public async Task 中间件_HTTP200无失败信封_Flush()
    {
        var bus = new RecordingLocalEventBus();
        var http = new DefaultHttpContext { Response = { StatusCode = 200 } };

        var middleware = new LocalEventFlushMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(http, bus, new RecordingLogger());

        Assert.Equal(1, bus.FlushCalls);
        Assert.Equal(0, bus.DiscardCalls);
    }

    [Fact]
    public async Task 中间件_Flush失败且响应未开始_改写信封()
    {
        var logger = new RecordingLogger();
        var bus = new RecordingLocalEventBus { FlushException = new InvalidOperationException("handler炸了") };
        var http = new DefaultHttpContext { Response = { Body = new MemoryStream(), StatusCode = 200 } };

        var middleware = new LocalEventFlushMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(http, bus, logger);

        Assert.Contains(logger.Errors, e => e.Contains("主业务已提交"));
        Assert.Equal(1, bus.DiscardCalls);
        Assert.True(http.Items.ContainsKey(VivRunDefine.ApiResultItemKey));
        var envelope = Assert.IsType<VivApiResult>(http.Items[VivRunDefine.ApiResultItemKey]);
        Assert.Equal((int)ApiResultCode.ServerError, envelope.Code);
    }

    [Fact]
    public async Task 作用域_成功Flush_失败Discard()
    {
        var bus = RealBus(new HostHandler());
        var scope = new LocalEventScope(bus, new RecordingLogger());

        await scope.RunAsync(async () => await bus.PublishAsync(new HostEvent()));
        Assert.Equal("host", Assert.Single(Hits));

        Hits.Clear();
        var bus2 = RealBus(new HostHandler());
        var scope2 = new LocalEventScope(bus2, new RecordingLogger());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope2.RunAsync(async () =>
            {
                await bus2.PublishAsync(new HostEvent());
                throw new InvalidOperationException("work炸了");
            }));
        Assert.Empty(Hits);
    }

    [Fact]
    public async Task 作用域_Flush失败_记日志并丢弃剩余()
    {
        var logger = new RecordingLogger();

        // 抛异常的排在前面，后面那个是会记 Hits 的 —— 两个一起进总线，
        // Assert.Empty(Hits) 才能证明「剩余事件被丢弃」，只放一个不记 Hits 的 handler 是恒真断言
        var bus = RealBus(new ThrowingHostHandler(), new HostHandler());
        var scope = new LocalEventScope(bus, logger);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.RunAsync(async () => await bus.PublishAsync(new HostEvent())));

        Assert.Equal("handler炸了", ex.Message);
        Assert.Contains(logger.Errors, e => e.Contains("主业务已完成"));
        Assert.Empty(Hits);
    }
}
