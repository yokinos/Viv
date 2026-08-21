using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts;
using Viv.Contracts.Interface;
using Viv.Delusion.Magic;

namespace Viv.Engine.Middleware
{
    public class HttpTrackMiddleware
    {
        private readonly RequestDelegate _next;
        public HttpTrackMiddleware(RequestDelegate next)
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
                    traceId = IdMagic.NextId(1).ToString();
                }

                // 用请求Id作为锁持有者Id，确保同一个请求的锁操作在同一个持有者Id下
                LockHolderContext.SetHolderId(traceId);

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
