using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Text;
using System.Text.Json;
using Viv.Contracts.Interface;
using Viv.EventContracts.Apex.Logging;
using Viv.Nana;

namespace Viv.Elysia.Filter
{
    /// <summary>
    /// 操作日志过滤器（标记在 Action 上自动记录操作日志）
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
            // 预置可变容器：AsyncLocal 只从父流向子，await 恢复仅还原引用不丢字段变化——
            // 若不预置，action 里 SetLog 的写入跨 await 流不回 filter 续段，Current 恒为 null
            ElysiaLogContextAccessor.Set(new OperationLogContext());

            await next();
            var opCtx = ElysiaLogContextAccessor.Current;
            if (opCtx == null || !opCtx.IsSet || !opCtx.IsRecord)
            {
                return;
            }

            var requestBody = await ReadRequestBodyAsync(context.HttpContext.Request);
            var responseBody = await ReadResponseBodyAsync(context.HttpContext.Response);

            // 发布[操作日志记录事件]
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
            });

            ElysiaLogContextAccessor.Clear();
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