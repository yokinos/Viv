using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Hosting;
using System;
using Viv.Contracts.Exceptions;
using Viv.Contracts.Interface;
using Viv.Delusion.Extension;
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
        private readonly IHostEnvironment _environment;

        public VivExceptionFilterAttribute(ILoggerContract logger, IHostEnvironment environment)
        {
            _logger = logger;
            _environment = environment;
        }

        public async Task OnExceptionAsync(ExceptionContext context)
        {
            if (context.ExceptionHandled)
                return;

            var ex = context.Exception;
            var realEx = ex.InnerException ?? ex;
            var httpContext = context.HttpContext;
            var traceId = httpContext.TraceIdentifier;
            var path = httpContext.Request.Path;
            var method = httpContext.Request.Method;

            _logger.Error("[全局异常] {Method} {Path} | RequestId: {RequestId} | Message: {Message}", realEx, method, path, traceId, realEx.Message);

            var output = new ExceptionOutput
            {
                Path = path,
                Method = method,
                Timestamp = DateTime.Now.ExtToString(),
                RequestId = traceId,
                StackTrace = _environment.IsDevelopment() ? realEx.StackTrace : null,
                ErrorCode = (realEx as IVivBusinessException)?.Code
            };

            context.Result = VivApiResult.ApiRsult(ApiResultCode.ServerError, null, output);
            context.ExceptionHandled = true;

            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// 异常响应输出模型
    /// </summary>
    public class ExceptionOutput
    {
        /// <summary>
        /// 请求路径
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// 请求方法
        /// </summary>
        public string Method { get; set; } = string.Empty;

        /// <summary>
        /// 发生时间（UTC）
        /// </summary>
        public string Timestamp { get; set; } = string.Empty;

        /// <summary>
        /// 请求追踪ID（用于日志关联）
        /// </summary>
        public string RequestId { get; set; } = string.Empty;

        /// <summary>
        /// 错误码（业务异常时返回）
        /// </summary>
        public int? ErrorCode { get; set; }

        /// <summary>
        /// 堆栈信息（仅开发环境返回）
        /// </summary>
        public string? StackTrace { get; set; }
    }
}