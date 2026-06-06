using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using Viv.Contracts.Exceptions;
using Viv.Emt;
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
        private readonly IEmtLogger _logger;

        /// <summary>
        /// 构造函数：依赖注入（消息生产者 + 日志组件）
        /// </summary>
        public VivExceptionFilterAttribute(IEmtLogger logger)
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

            if (ex is InvalidTokenException)
            {
                _logger.Warning($"[Token无效]请求地址：{path}，信息：{ex.Message}");
                context.HttpContext.Response.StatusCode = 401;
                context.Result = VivApiResult.ApiRsult(ResultCode.TokenInvalid, "Token无效或已过期");
                context.ExceptionHandled = true;
                return;
            }
            else
            {
                var realEx = ex.InnerException ?? ex;
                _logger.Error($"[全局未捕获异常]请求地址：{path}，消息：{ex.Message}", realEx);
                context.Result = VivApiResult.ApiRsult(ResultCode.ServerError, "服务器异常");
                context.ExceptionHandled = true;
            }

            await Task.CompletedTask;
        }
    }
}