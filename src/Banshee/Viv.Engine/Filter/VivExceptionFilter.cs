using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using Viv.Contracts.Exceptions;
using Viv.Log;
using Viv.Nana;

namespace Viv.Engine.Filter
{
    /// <summary>
    /// Viv 框架全局异步异常过滤器
    /// 作用：捕获控制器中所有未处理的异常，统一包装返回、记录日志、处理业务异常
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class VivExceptionFilterAttribute : Attribute, IAsyncExceptionFilter
    {
        private readonly ILoggerContract _logger;

        /// <summary>
        /// 构造函数：依赖注入（消息生产者 + 日志组件）
        /// </summary>
        public VivExceptionFilterAttribute(ILoggerContract logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 异步异常处理核心方法
        /// </summary>
        public async Task OnExceptionAsync(ExceptionContext context)
        {
            // 如果异常已经被处理，直接跳过
            if (context.ExceptionHandled)
                return;

            // 获取当前异常对象
            var ex = context.Exception;
            var path = context.HttpContext.Request.Path;

            var realEx = ex.InnerException ?? ex;
            _logger.Error($"[全局未捕获异常]请求地址：{path}，消息：{ex.Message}", realEx);
            context.Result = VivApiResult.ApiRsult(ApiResultCode.ServerError);
            context.ExceptionHandled = true;

            await Task.CompletedTask;
        }
    }
}