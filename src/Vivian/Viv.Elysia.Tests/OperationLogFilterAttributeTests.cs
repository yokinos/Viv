using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Elysia.Attributes;
using Viv.Elysia.Filter;
using Viv.Engine;
using Viv.Entity.Enums;
using Viv.EventContracts.Apex.Logging;
using Viv.Nana;
using Viv.Nana.Core;

namespace Viv.Elysia.Tests
{
    /// <summary>
    /// OperationLogFilter 的 [OperationLog] 特性支持 + 预置容器语义。
    /// 回归点：入口「有 Current 优先，没有再读特性播种」；特性门控按 VivApiResult.Code（业务信封码）。
    /// </summary>
    public class OperationLogFilterAttributeTests
    {
        private sealed class StubContext : IVivContext
        {
            public long AppId => 1;
            public long SubjectId => 2;
            public long UserId => 99;
            public string TraceId => "req-1";
            public void SetSnapshot(VivContextContent model) { }
            public void Clear() { }
            public VivContextContent? GetRawSnapshot() => null;
        }

        private sealed class StubPublisher : IVivEventPublisher
        {
            public List<NanaEvent> Published { get; } = new();

            public ValueTask<bool> PublishAsync<T>(T content, CancellationToken cancellationToken = default) where T : NanaEvent
            {
                Published.Add(content);
                return ValueTask.FromResult(true);
            }

            public ValueTask<bool> PublishDelayAsync<T>(TimeSpan delayTTL, T content, CancellationToken cancellationToken = default) where T : NanaEvent
                => ValueTask.FromResult(true);

            public ValueTask<bool> PublishDelayAsync<T>(TimeSpan delayTTL, NanaEnvelope<T> envelope, CancellationToken cancellationToken = default) where T : NanaEvent
                => ValueTask.FromResult(true);
        }

        private sealed class SampleController
        {
            [OperationLog(EmOperationModule.User, EmOperationType.Login)]
            public void WithLoginAttr() { }

            [OperationLog(EmOperationModule.User, EmOperationType.Login, 200, -200)]
            public void WithLoginAndErrorCodes() { }

            public void NoAttr() { }
        }

        private static ControllerActionDescriptor DescriptorFor(string methodName)
            => new() { MethodInfo = typeof(SampleController).GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)! };

        private static (OperationLogFilterAttribute filter, ActionExecutingContext ctx, ActionExecutionDelegate next, StubPublisher publisher)
            Create(ActionDescriptor descriptor, VivApiResult? result, Action? inAction = null)
        {
            var publisher = new StubPublisher();
            var filter = new OperationLogFilterAttribute(publisher, new StubContext());
            var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), descriptor);
            var filters = new List<IFilterMetadata>();
            var ctx = new ActionExecutingContext(actionContext, filters, new Dictionary<string, object?>(), null!);

            ActionExecutionDelegate next = () =>
            {
                inAction?.Invoke();
                // .NET 10：ActionExecutedContext 3 参构造的第三参是 controller（object），Result 必须走属性 setter
                return Task.FromResult(new ActionExecutedContext(actionContext, filters, null!) { Result = result });
            };

            return (filter, ctx, next, publisher);
        }

        [Fact]
        public async Task 有特性_成功码200_发布且Description取结果Message()
        {
            ElysiaLogContextAccessor.Clear();
            var (filter, ctx, next, publisher) =
                Create(DescriptorFor(nameof(SampleController.WithLoginAttr)), VivApiResult.Success("登录成功"));

            await filter.OnActionExecutionAsync(ctx, next);

            var evt = Assert.IsType<UserOperationLogEvent>(Assert.Single(publisher.Published));
            Assert.Equal(EmOperationModule.User, evt.Module);
            Assert.Equal(EmOperationType.Login, evt.Operation);
            Assert.Equal("登录成功", evt.Description);
            Assert.Equal(99, evt.UserId);
        }

        [Fact]
        public async Task 有特性_码不在Codes_跳过()
        {
            ElysiaLogContextAccessor.Clear();
            // 默认 Codes=[200]，业务信封码 -200（失败）不在内 → 不记录
            var (filter, ctx, next, publisher) =
                Create(DescriptorFor(nameof(SampleController.WithLoginAttr)), new VivApiResult(-200, "登录失败"));

            await filter.OnActionExecutionAsync(ctx, next);

            Assert.Empty(publisher.Published);
        }

        [Fact]
        public async Task 有特性_码在Codes_发布()
        {
            ElysiaLogContextAccessor.Clear();
            // Codes=[200,-200]，-200 在内 → 记录
            var (filter, ctx, next, publisher) =
                Create(DescriptorFor(nameof(SampleController.WithLoginAndErrorCodes)), new VivApiResult(-200, "登录失败"));

            await filter.OnActionExecutionAsync(ctx, next);

            Assert.Single(publisher.Published);
        }

        [Fact]
        public async Task 无特性_业务SetLog_发布()
        {
            ElysiaLogContextAccessor.Clear();
            var (filter, ctx, next, publisher) = Create(
                DescriptorFor(nameof(SampleController.NoAttr)), VivApiResult.Success("ok"),
                inAction: () => ElysiaLogContextAccessor.SetLog(EmOperationModule.User, EmOperationType.Add, "业务描述"));

            await filter.OnActionExecutionAsync(ctx, next);

            var evt = Assert.IsType<UserOperationLogEvent>(Assert.Single(publisher.Published));
            Assert.Equal(EmOperationModule.User, evt.Module);
            Assert.Equal(EmOperationType.Add, evt.Operation);
            Assert.Equal("业务描述", evt.Description);
        }

        [Fact]
        public async Task 无特性_未SetLog_跳过()
        {
            ElysiaLogContextAccessor.Clear();
            var (filter, ctx, next, publisher) =
                Create(DescriptorFor(nameof(SampleController.NoAttr)), VivApiResult.Success("ok"));

            await filter.OnActionExecutionAsync(ctx, next);

            Assert.Empty(publisher.Published);
        }

        [Fact]
        public async Task 外部已有Current_优先于特性()
        {
            ElysiaLogContextAccessor.Clear();
            // 外部已设上下文（如外层过滤器），即使 action 标了 [OperationLog(User, Login)] 也不覆盖
            ElysiaLogContextAccessor.Set(new OperationLogContext(EmOperationModule.User, EmOperationType.Add) { IsSet = true });
            var (filter, ctx, next, publisher) =
                Create(DescriptorFor(nameof(SampleController.WithLoginAttr)), VivApiResult.Success("ok"));

            await filter.OnActionExecutionAsync(ctx, next);

            var evt = Assert.IsType<UserOperationLogEvent>(Assert.Single(publisher.Published));
            Assert.Equal(EmOperationType.Add, evt.Operation);
        }
    }
}
