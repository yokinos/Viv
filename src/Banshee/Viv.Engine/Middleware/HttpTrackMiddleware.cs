using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Interface;
using Viv.Delusion.Magic;
using Viv.Redis;

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
                var requestId = context.Request.Headers[VivRunDefine.VivTraceIdHeader].FirstOrDefault();
                if (string.IsNullOrEmpty(requestId))
                {
                    requestId = IdMagic.NextId(1).ToString();
                }

                // 用请求Id作为锁持有者Id，确保同一个请求的锁操作在同一个持有者Id下
                LockHolderContext.GenerateHolderId(requestId);

                context.TraceIdentifier = requestId;
                context.Items[VivRunDefine.ContextRequestId] = requestId;
                context.Response.Headers[VivRunDefine.VivTraceIdHeader] = requestId;

                using (Serilog.Context.LogContext.PushProperty(VivRunDefine.ContextRequestId, requestId))
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
