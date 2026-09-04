using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts;
using Viv.Contracts.Interface;
using Viv.Delusion.Magic;

namespace Viv.Engine.Middleware
{
    /// <summary>
    /// [中间件] 请求追踪中间件
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

                // 生成一个新的 HolderId 并设置到 LockHolderContext 中
                LockHolderContext.GenerateHolderId();

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
