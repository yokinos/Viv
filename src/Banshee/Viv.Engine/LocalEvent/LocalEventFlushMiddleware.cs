using Microsoft.AspNetCore.Http;
using System;
using System.Threading;
using System.Threading.Tasks;
using Viv.Contracts.Interface;

namespace Viv.Engine.LocalEvent
{
    /// <summary>
    /// [中间件] 本地事件分发触发点 —— 兜底路径。
    ///
    /// MVC 控制器走 LocalEventFlushFilterAttribute（能看信封码，判定更准）；
    /// 本中间件负责非 MVC 端点：gRPC 服务、SignalR hub、health/alive。
    ///
    /// <b>FlushAsync / Discard 均幂等</b>，所以 MVC 已处理过的请求在这里是 no-op ——
    /// 它只是把非 MVC 端点残留的事件捞出来。
    ///
    /// 代价诚实说：非 MVC 端点只能按 HTTP 状态码判定成败，粒度粗于 MVC 路径
    ///（业务信封失败在这里表现为 HTTP 200，会被当作成功）。
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
