using Microsoft.AspNetCore.Http;
using Viv.Contracts;
using Viv.Contracts.Enums;
using Viv.Delusion.Magic;
using Viv.Engine.Power;

namespace Viv.Engine.Middleware
{
    /// <summary>
    /// [中间件] 请求追踪中间件。
    ///
    /// <para>
    /// traceId：<b>一律服务端生成</b>。网关是信任根，客户端传什么都不认，本进程生成后写进
    /// 转发请求头，下游复用同一个值。以前只写响应头、不写请求头，YARP 转出去那一跳没有它，
    /// 下游各自再造一条 —— 一个请求穿过 4 个服务就在日志里留下 4 条互不相干的链路。
    /// </para>
    ///
    /// <para>
    /// holderId：下游验签通过且带 x-viv-holder-id 才采用上游值；网关始终本进程生成（信任根）。
    /// </para>
    ///
    /// <para>
    /// <b>两者信任强度不同，是有意为之。</b>holderId 会进 <c>VivContextContent</c> 参与分布式锁持有者判定，
    /// 所以必须走 <c>x-request-token</c> 的 HMAC 验签；traceId 不带任何权限语义，只用于日志串联，
    /// 它不进上下文、不影响鉴权与数据过滤
    /// 全部验签失败，代价远大于收益。直连下游伪造 traceId 只能污染日志，而能直连下游的攻击者
    /// 本来就能伪造日志内容。
    /// </para>
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
                var isGateway = VivEngine.VivOptions?.EnvOption?.ServiceType == VivServiceType.Gateway;

                string traceId;
                if (isGateway)
                {
                    traceId = IdMagic.NextIdString();
                    context.Request.Headers[VivRunDefine.VivTraceIdHeader] = traceId;
                }
                else
                {
                    // 下游只认上游写进来的值；没经过网关的（直连、Worker、定时任务）自己起一条
                    var upstreamTraceId = context.Request.Headers[VivRunDefine.VivTraceIdHeader].FirstOrDefault();
                    traceId = string.IsNullOrEmpty(upstreamTraceId) ? IdMagic.NextIdString() : upstreamTraceId;
                }

                // 网关是 holderId 信任根：先剥离客户端头再回填当前值，此处一律生成。
                // 下游 API 才验签采纳上游（含网关）签发的 x-viv-holder-id。
                if (!isGateway && RequestTokenResolver.TryGetTrustedHolderId(context.Request.Headers, out var holderId))
                {
                    VivCallContext.SetHolderId(holderId);
                }
                else
                {
                    VivCallContext.GenerateHolderId();
                }

                // gRPC / MagicOnion 客户端拦截器在 Viv.Keke 里，够不到 HttpContext，
                // 只能靠这个静态访问器把 traceId 带进下游调用的 metadata
                VivCallContext.SetTraceId(traceId);

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
                // traceId 与 holderId 一起清：AsyncLocal 留着值会让线程池上的下一个请求继承
                VivCallContext.Clear();
            }
        }
    }
}
