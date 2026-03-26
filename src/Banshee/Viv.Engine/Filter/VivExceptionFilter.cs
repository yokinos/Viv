using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using Viv.Contracts.Exceptions;
using Viv.Log.VivLogger;
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
        private readonly IVivLogger _vivLogger;

        /// <summary>
        /// 构造函数：依赖注入（消息生产者 + 日志组件）
        /// </summary>
        public VivExceptionFilterAttribute(IVivLogger vivLogger)
        {
            _vivLogger = vivLogger;
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

            if (ex is VivConnectionException)
            {
                _vivLogger.Error($"[连接异常]请求地址：{path}", ex);
                context.ExceptionHandled = true;
                return;
            }
            else if (ex is InvalidTokenException)
            {
                _vivLogger.Warn($"[Token无效]请求地址：{path}，信息：{ex.Message}");
                context.HttpContext.Response.StatusCode = 401;
                context.Result = new VivApiResult(-200, "Token无效或已过期");
                context.ExceptionHandled = true;
                return;
            }
            else
            {
                var realEx = ex.InnerException ?? ex;
                _vivLogger.Error($"[全局未捕获异常]请求地址：{path}，消息：{ex.Message}", realEx);
                context.Result = new VivApiResult(500, $"服务器异常：{ex.Message}", null);
                context.ExceptionHandled = true;
            }

            await Task.CompletedTask;
        }
    }
}