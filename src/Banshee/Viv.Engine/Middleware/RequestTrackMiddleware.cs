using Microsoft.AspNetCore.Http;
using Viv.Contracts;
using Viv.Contracts.Enums;
using Viv.Delusion.Magic;
using Viv.Engine.Power;

namespace Viv.Engine.Middleware
{
    /// <summary>
    /// [中间件] 请求追踪中间件。
    /// holderId：下游验签通过且带 x-viv-holder-id 才采用上游值；网关始终本进程生成（信任根）。
    /// 客户端无法伪造。
    /// </summary>
    public class RequestTrackMiddleware
    {
        private readonly RequestDelegate _next;
        public RequestTrackMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            try
            {
                var traceId = context.Request.Headers[VivRunDefine.VivTraceIdHeader].FirstOrDefault();
                if (string.IsNullOrEmpty(traceId))
                {
                    traceId = IdMagic.NextId(1024).ToString();
                }

                // 网关是 holderId 信任根：先剥离客户端头再回填当前值，此处一律生成。
                // 下游 API 才验签采纳上游（含网关）签发的 x-viv-holder-id。
                var isGateway = VivEngine.VivOptions?.EnvOption?.ServiceType == VivServiceType.Gateway;
                if (!isGateway && RequestTokenResolver.TryGetTrustedHolderId(context.Request.Headers, out var holderId))
                {
                    LockHolderContext.SetHolderId(holderId);
                }
                else
                {
                    LockHolderContext.GenerateHolderId();
                }

                context.TraceIdentifier = traceId;
                context.Items[VivRunDefine.ContextTraceId] = traceId;
                context.Response.Headers[VivRunDefine.VivTraceIdHeader] = traceId;

                using (Serilog.Context.LogContext.PushProperty(VivRunDefine.ContextTraceId, traceId))
                {
                    await _next(context).ConfigureAwait(false);
                }
            }
            finally
            {
                LockHolderContext.Clear();
            }
        }
    }
}
