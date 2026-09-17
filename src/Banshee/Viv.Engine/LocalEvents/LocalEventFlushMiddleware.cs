using Microsoft.AspNetCore.Http;
using System;
using System.Threading;
using System.Threading.Tasks;
using Viv.Contracts.Interface;

namespace Viv.Engine.LocalEvents
{
    /// <summary>
    /// 本地事件分发触发点 —— 兜底路径，覆盖非 MVC 端点（gRPC 服务、SignalR hub、health/alive）。
    /// MVC 控制器走 LocalEventFlushFilterAttribute，那条路能看信封码，判定更准。
    ///
    /// FlushAsync / Discard 都幂等，所以 MVC 已处理过的请求到这里是 no-op。
    ///
    /// 代价是这里只能按 HTTP 状态码判定成败，粒度粗于 MVC 路径 ——
    /// 业务信封失败在这里表现为 HTTP 200，会被当作成功。
    /// </summary>
    public class LocalEventFlushMiddleware
    {
        private readonly RequestDelegate _next;

        public LocalEventFlushMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, IVivLocalEventBus localEventBus)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(localEventBus);

            await _next(context).ConfigureAwait(false);

            if (context.Response.StatusCode >= 400)
            {
                localEventBus.Discard();
                return;
            }

            // 同 LocalEventFlushFilterAttribute：刻意传 None，不用 RequestAborted —— handler 必须跑完
            await localEventBus.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
