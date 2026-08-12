using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Viv.Elysia.Filter;
using Viv.Elysia.Interface;
using Viv.Engine;

namespace Viv.Elysia.Tests
{
    public class RequestFilterAttributeTests
    {
        private sealed class FakeRequest : IApiRequest
        {
            private readonly string _error;
            public FakeRequest(string error) => _error = error;
            public string Validate() => _error;
        }

        private sealed class ExecutedFlag
        {
            public bool Value;
        }

        private static (ActionExecutingContext ctx, ActionExecutionDelegate next, ExecutedFlag executed) CreateContext(
            IDictionary<string, object?> arguments)
        {
            var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
            var filters = new List<IFilterMetadata>();
            var ctx = new ActionExecutingContext(actionContext, filters, arguments, null!);
            var executed = new ExecutedFlag();

            ActionExecutionDelegate next = () =>
            {
                executed.Value = true;
                return Task.FromResult(new ActionExecutedContext(actionContext, filters, null!));
            };

            return (ctx, next, executed);
        }

        [Fact]
        public async Task NoArguments_InvokesNext()
        {
            var (ctx, next, executed) = CreateContext(new Dictionary<string, object?>());
            await new RequestFilterAttribute().OnActionExecutionAsync(ctx, next);
            Assert.True(executed.Value);
            Assert.Null(ctx.Result);
        }

        [Fact]
        public async Task NullArgument_ReturnsParamMissing()
        {
            var (ctx, next, executed) = CreateContext(new Dictionary<string, object?> { ["req"] = null });
            await new RequestFilterAttribute().OnActionExecutionAsync(ctx, next);
            Assert.False(executed.Value);
            var result = Assert.IsType<VivApiResult>(ctx.Result);
            Assert.Equal((int)ApiResultCode.ParamMissing, result.Code);
            Assert.Contains("req", result.Message);
        }

        [Fact]
        public async Task InvalidIApiRequest_ReturnsError()
        {
            var (ctx, next, executed) = CreateContext(new Dictionary<string, object?> { ["req"] = new FakeRequest("bad param") });
            await new RequestFilterAttribute().OnActionExecutionAsync(ctx, next);
            Assert.False(executed.Value);
            var result = Assert.IsType<VivApiResult>(ctx.Result);
            Assert.Equal((int)ApiResultCode.Error, result.Code);
            Assert.Equal("bad param", result.Message);
        }

        [Fact]
        public async Task ValidIApiRequest_InvokesNext()
        {
            var (ctx, next, executed) = CreateContext(new Dictionary<string, object?> { ["req"] = new FakeRequest("") });
            await new RequestFilterAttribute().OnActionExecutionAsync(ctx, next);
            Assert.True(executed.Value);
            Assert.Null(ctx.Result);
        }

        [Fact]
        public async Task NonRequestArgument_InvokesNext()
        {
            var (ctx, next, executed) = CreateContext(new Dictionary<string, object?> { ["id"] = 123 });
            await new RequestFilterAttribute().OnActionExecutionAsync(ctx, next);
            Assert.True(executed.Value);
            Assert.Null(ctx.Result);
        }
    }
}
