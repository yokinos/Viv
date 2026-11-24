using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Viv.Log;

namespace Viv.Elysia.Logging
{
    /// <summary>
    /// <see cref="IOperationLogScope"/> 的默认实现：把业务补充的信息写进当前请求的
    /// <see cref="OperationLogBag"/>，由过滤器在 action 结束后统一取走。
    ///
    /// <para>Scoped，随请求创建；不用注入也不影响框架工作（过滤器自己在 Items 上建包）。</para>
    /// </summary>
    internal sealed class OperationLogScope : IOperationLogScope
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IServiceProvider _services;

        public OperationLogScope(IHttpContextAccessor httpContextAccessor, IServiceProvider services)
        {
            _httpContextAccessor = httpContextAccessor;
            _services = services;
        }

        public void Describe(string description)
        {
            var bag = Bag();
            if (bag is null) return;

            bag.Description = description;
        }

        public void Target(string type, object? id)
        {
            var bag = Bag();
            if (bag is null) return;

            bag.TargetType = type;
            bag.TargetId = id?.ToString();
        }

        public void Skip()
        {
            var bag = Bag();
            if (bag is null) return;

            bag.Skip = true;
        }

        /// <summary>
        /// 取当前请求的包；没有 HttpContext 时记一条 Warning 并放弃 —— 不抛异常（业务代码不该因为这个
        /// 失败），也不能静默（那正是旧 <c>SetLog</c> 在 Worker 里的毛病：写了，没人读，也不报错）。
        /// </summary>
        private OperationLogBag? Bag()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext is not null)
            {
                return OperationLogBag.Ensure(httpContext);
            }

            _services.GetService<ILoggerContract>()?.Warning(
                "IOperationLogScope 在没有 HttpContext 的上下文被调用，本次日志补充已忽略；" +
                "Worker / 定时任务请直接注入 IOperationLogWriter 主动写日志。");
            return null;
        }
    }
}
