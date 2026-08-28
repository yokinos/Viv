using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
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
    /// 扩展点：支持通过异常类型映射到不同的错误码和响应数据
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class VivExceptionFilterAttribute : Attribute, IAsyncExceptionFilter
    {
        private readonly ILoggerContract _logger;
        private readonly IHostEnvironment _environment;

        /// <summary>
        /// 异常类型 → (错误码, 数据工厂) 映射表
        /// 用于区分不同类型的异常，返回不同的 ApiResultCode
        /// </summary>
        private static readonly Dictionary<Type, (ApiResultCode Code, Func<Exception, object?> DataFactory)> _exceptionHandlers = new()
        {
            // 业务异常：返回 ServerError（操作失败，客户端不应自动重试）
            [typeof(VivBusinessException)] = (ApiResultCode.ServerError, ex => (ex as VivBusinessException)?.Output),

            // 分布式锁异常：返回 BusyError（操作繁忙，客户端可以稍后重试）
            [typeof(DistributedLockException)] = (ApiResultCode.BusyError, _ => null),
        };

        public VivExceptionFilterAttribute(ILoggerContract logger, IHostEnvironment environment)
        {
            _logger = logger;
            _environment = environment;
        }

        /// <summary>
        /// 异常处理入口（由 ASP.NET Core 调用）
        /// </summary>
        public async Task OnExceptionAsync(ExceptionContext context)
        {
            // 如果已有其他过滤器处理过，则跳过
            if (context.ExceptionHandled)
                return;

            var ex = context.Exception;
            var realEx = ex.InnerException ?? ex;      // 解包内部异常
            var httpContext = context.HttpContext;
            var traceId = httpContext.TraceIdentifier;
            var path = httpContext.Request.Path;
            var method = httpContext.Request.Method;

            // 记录日志（结构化日志，包含堆栈）
            // 对于分布式锁异常，可以降级为 Warning，避免告警风暴（此处保留 Error，可根据需要调整）
            // 分布式锁异常只记 Warning，避免告警风暴
            if (realEx is DistributedLockException)
            {
                _logger.Warning("[全局异常] {Method} {Path} | RequestId: {RequestId} | Message: {Message}", realEx, method, path, traceId, realEx.Message);
            }
            else
            {
                _logger.Error("[全局异常] {Method} {Path} | RequestId: {RequestId} | Message: {Message}", realEx, method, path, traceId, realEx.Message);
            }

            // 根据异常类型构建响应
            context.Result = BuildErrorResponse(realEx, httpContext);
            context.ExceptionHandled = true;

            await Task.CompletedTask;
        }

        /// <summary>
        /// 根据异常类型构建 IActionResult
        /// </summary>
        private VivApiResult BuildErrorResponse(Exception ex, HttpContext httpContext)
        {
            // 如果异常类型在映射表中，使用对应的错误码和数据
            if (_exceptionHandlers.TryGetValue(ex.GetType(), out var handler))
            {
                // 使用映射的 Code和数据
                return VivApiResult.ApiResult(handler.Code, ex.Message, handler.DataFactory(ex));
            }

            // 未知异常（未在映射表中定义的）→ 统一返回 ServerError
            // 并在开发环境附加堆栈信息，便于调试
            var output = new ExceptionOutput
            {
                Path = httpContext.Request.Path,
                Method = httpContext.Request.Method,
                Timestamp = DateTime.UtcNow.ExtToString(),   // 使用 UTC 时间，便于分布式系统
                RequestId = httpContext.TraceIdentifier,
                StackTrace = _environment.IsDevelopment() ? ex.StackTrace : null,
                ErrorCode = (ex as IVivBusinessException)?.Code   // 如果业务异常未注册，但实现了此接口
            };

            return VivApiResult.ApiResult(ApiResultCode.ServerError, ex.Message, output);
        }

        /// <summary>
        /// 异常响应输出模型（用于未知异常）
        /// </summary>
        public class ExceptionOutput
        {
            /// <summary>
            /// 请求路径
            /// </summary>
            public string Path { get; set; } = string.Empty;

            /// <summary>
            /// 请求方法（GET/POST/...）
            /// </summary>
            public string Method { get; set; } = string.Empty;

            /// <summary>
            /// 发生时间（UTC 格式）
            /// </summary>
            public string Timestamp { get; set; } = string.Empty;

            /// <summary>
            /// 请求追踪ID，用于日志关联
            /// </summary>
            public string RequestId { get; set; } = string.Empty;

            /// <summary>
            /// 错误码（如果异常实现了 IVivBusinessException）
            /// </summary>
            public int? ErrorCode { get; set; }

            /// <summary>
            /// 堆栈信息（仅开发环境返回）
            /// </summary>
            public string? StackTrace { get; set; }
        }
    }
}