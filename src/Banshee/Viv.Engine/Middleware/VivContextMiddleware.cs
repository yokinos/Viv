using Microsoft.AspNetCore.Http;
using System;
using System.Text;
using System.Threading.Tasks;
using Viv.Contracts.Interface;
using Viv.Delusion.Magic;
using Viv.Redis;

namespace Viv.Engine.Middleware
{
    /// <summary>
    /// [中间件] Viv 上下文中间件
    /// </summary>
    public class VivContextMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IVivContextProvider _contextProvider;

        public VivContextMiddleware(RequestDelegate next, IVivContextProvider contextProvider)
        {
            _next = next;
            _contextProvider = contextProvider;
        }

        public async Task InvokeAsync(HttpContext context, IVivContext vivContext)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(vivContext);

            try
            {
                LockHolderContext.ResetHolderId();

                var requestId = context.Request.Headers["X-Request-Id"].FirstOrDefault();
                if (string.IsNullOrEmpty(requestId))
                {
                    requestId = IdMagic.NextId(1).ToString();
                }

                context.TraceIdentifier = requestId;
                context.Items["RequestId"] = requestId;
                context.Response.Headers["X-Request-Id"] = requestId;

                using (Serilog.Context.LogContext.PushProperty("RequestId", requestId))
                {
                    if (!_contextProvider.ShouldSkip(context))
                    {
                        var model = await _contextProvider.GetContextAsync(context).ConfigureAwait(false);
                        if (model == null)
                        {
                            await context.SetApiResponseAsync(ApiResultCode.TokenEmpty);
                            return;
                        }

                        vivContext.SetSnapshot(model);
                    }

                    await _next(context).ConfigureAwait(false);
                }
            }
            finally
            {
                LockHolderContext.Clear();
                vivContext.Clear();
            }
        }
    }
}