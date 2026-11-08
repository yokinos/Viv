using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Viv.Contracts.Models;
using Viv.Engine.Power;
using Viv.Kinna.Ports;

namespace Viv.Herta.Link.Adapters
{
    /// <summary>
    /// 身份解析：网关头优先，直连时回落到 IM 自己签发的 JWT。
    ///
    /// 这段逻辑原来写在 <see cref="Hubs.ChatHub.OnConnectedAsync"/> 里，现在抽出来给两套传输共用——
    /// 原生 WebSocket 端点没法和 Hub 共享代码，复制一份迟早会漏改一边。
    /// </summary>
    public sealed class HertaIdentityResolver<THub> : IKinnaIdentityResolver<THub> where THub : Hub
    {
        public async Task<VivContextContent?> ResolveAsync(HttpContext context)
        {
            var identity = RequestTokenResolver.GetContextFromHeaders(context);
            if (identity != null)
            {
                return identity;
            }

            return await RequestTokenResolver.GetContextFromTokenAsync(context).ConfigureAwait(false);
        }
    }
}
