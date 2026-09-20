using Microsoft.AspNetCore.Http;
using System;
using System.Threading;
using System.Threading.Tasks;
using Viv.Contracts.Interface;
using Viv.Log;

namespace Viv.Engine.LocalEvents
{
    /// <summary>
    /// 本地事件分发触发点 —— 兜底路径，覆盖非 MVC 端点（gRPC 服务、SignalR hub、health/alive）。
    /// MVC 控制器走 LocalEventFlushFilterAttribute，那条路能看信封码，判定更准。
    ///
    /// FlushAsync / Discard 都幂等，所以 MVC 已处理过的请求到这里是 no-op。
    ///
    /// 除 HTTP 状态码外，还会读 <see cref="VivRunDefine.ApiResultItemKey"/> 里写出的业务信封：
    /// 非 MVC 路径常是「HTTP 200 + 信封非 2xx」，只看状态码会误 Flush。
    /// 响应已经开始后无法改写信封，Flush 失败只记日志并丢弃剩余事件。
    /// </summary>
    public class LocalEventFlushMiddleware
    {
        private readonly RequestDelegate _next;

        public LocalEventFlushMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, IVivLocalEventBus localEventBus, ILoggerContract logger)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(localEventBus);
            ArgumentNullException.ThrowIfNull(logger);

            await _next(context).ConfigureAwait(false);

            if (context.Response.StatusCode >= 400 || IsFailedEnvelope(context))
            {
                localEventBus.Discard();
                return;
            }

            try
            {
                // 同 LocalEventFlushFilterAttribute：刻意传 None，不用 RequestAborted —— handler 必须跑完
                await localEventBus.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                localEventBus.Discard();
                logger.Error("主业务已提交，本地事件分发失败。已丢弃剩余事件。", ex);

                if (!context.Response.HasStarted)
                {
                    await context.SetApiResponseAsync(ApiResultCode.ServerError).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// 业务信封失败（HTTP 仍可能是 200）。信封由 <see cref="VivApiResult.ExecuteResultAsync"/>
        /// 或 <c>SetApiResponseAsync</c> 写入 <see cref="HttpContext.Items"/>。
        /// </summary>
        private static bool IsFailedEnvelope(HttpContext context)
        {
            if (!context.Items.TryGetValue(VivRunDefine.ApiResultItemKey, out var stored))
                return false;

            return FailDetector.IsFailed(stored);
        }
    }
}
