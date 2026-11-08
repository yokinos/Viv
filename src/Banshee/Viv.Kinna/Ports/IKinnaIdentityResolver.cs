using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Viv.Contracts.Models;

namespace Viv.Kinna.Ports
{
    /// <summary>
    /// 身份解析。框架层没有网关头和 JWT 的验签实现（那在 Engine 里），
    /// 所以把「从这次 HTTP 请求取出可信身份」抽成端口，由宿主注入。
    ///
    /// SignalR 的 Hub 和原生 WebSocket 端点都必须走同一个实现，
    /// 否则换一种传输就能换一套身份判定。
    /// </summary>
    public interface IKinnaIdentityResolver<THub> where THub : Hub
    {
        /// <summary>
        /// 取不到可信身份时返回 null，不要抛异常——没带 token 是常态。
        /// </summary>
        Task<VivContextContent?> ResolveAsync(HttpContext context);
    }
}
