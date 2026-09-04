using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Viv.Contracts.Interface;
using Viv.Elysia.Attributes;
using Viv.Engine;
using Viv.EventContracts.Apex.Logging;
using Viv.Nana;

namespace Viv.Elysia.Filter
{
    /// <summary>
    /// 操作日志过滤器
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public class OperationLogFilterAttribute : Attribute, IAsyncActionFilter
    {
        private readonly IVivEventPublisher _eventPublisher;
        private readonly IVivContext _vivContext;

        public OperationLogFilterAttribute(IVivEventPublisher eventPublisher, IVivContext vivContext)
        {
            _eventPublisher = eventPublisher;
            _vivContext = vivContext;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            try
            {
                // 有现有上下文（外部已 Set/SetLog）→ 优先，不覆盖；
                // 没有 → 读 [OperationLog] 特性播种（声明式 opt-in，IsSet=true）；无特性则预置空容器等业务 SetLog。
                // 预置可变容器：AsyncLocal 只从父流向子，action 里 SetLog 改的是容器字段（引用不变），跨 await 后仍可读
                OperationLogAttribute? attr = null;
                if (ElysiaLogContextAccessor.Current == null)
                {
                    attr = (context.ActionDescriptor as ControllerActionDescriptor)?.MethodInfo.GetCustomAttribute<OperationLogAttribute>();
                    ElysiaLogContextAccessor.Set(attr != null ? new OperationLogContext(attr.Module, attr.Operation) { IsSet = true } : new OperationLogContext());
                }

                var executed = await next();

                var opCtx = ElysiaLogContextAccessor.Current;
                if (opCtx == null)
                {
                    // 兜底：上下文被清空时回退读特性
                    attr = (context.ActionDescriptor as ControllerActionDescriptor)?.MethodInfo.GetCustomAttribute<OperationLogAttribute>();
                    if (attr == null)
                    {
                        return;
                    }

                    opCtx = new OperationLogContext(attr.Module, attr.Operation) { IsSet = true };
                    ElysiaLogContextAccessor.Set(opCtx);
                }

                if (!opCtx.IsSet || !opCtx.IsRecord)
                {
                    return;
                }

                // 特性门控（仅 filter 按特性播种时生效）：result 为 VivApiResult 且业务信封码不在 Codes 内 → 不记录（默认 [200] 只记成功）
                if (attr != null && executed.Result is VivApiResult result && attr.Codes.Length > 0 && !attr.Codes.Contains(result.Code))
                {
                    return;
                }

                // Description 缺省取结果的 Message（特性注释：以返回结果的 Message 为日志内容）
                if (attr != null && string.IsNullOrEmpty(opCtx.Description) && executed.Result is VivApiResult r2)
                {
                    opCtx.Description = r2.Message;
                }

                var requestBody = await ReadRequestBodyAsync(context.HttpContext.Request);
                var responseBody = await ReadResponseBodyAsync(context.HttpContext.Response);

                await _eventPublisher.PublishAsync(new UserOperationLogEvent()
                {
                    Description = opCtx.Description,
                    Module = opCtx.Module,
                    Operation = opCtx.Operation,
                    RequestJson = requestBody,
                    ResponseJson = responseBody,
                    UserId = _vivContext.UserId,
                    IsJob = false,
                    Priority = 0
                }).ConfigureAwait(false);
            }
            finally
            {
                ElysiaLogContextAccessor.Clear();
            }
        }

        private static async Task<string> ReadRequestBodyAsync(HttpRequest request)
        {
            try
            {
                request.EnableBuffering();
                request.Body.Position = 0;
                using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
                var body = await reader.ReadToEndAsync();
                request.Body.Position = 0;
                return body;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static async Task<string> ReadResponseBodyAsync(HttpResponse response)
        {
            try
            {
                var originalBodyStream = response.Body;
                using var memoryStream = new MemoryStream();
                response.Body = memoryStream;
                await memoryStream.CopyToAsync(originalBodyStream);
                memoryStream.Position = 0;
                var body = await new StreamReader(memoryStream, Encoding.UTF8).ReadToEndAsync();
                response.Body = originalBodyStream;
                return body;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}