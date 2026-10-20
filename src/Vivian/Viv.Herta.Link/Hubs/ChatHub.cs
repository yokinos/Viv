using Microsoft.AspNetCore.SignalR;
using Viv.Engine.Power;
using Viv.Herta.Link.Contracts;

namespace Viv.Herta.Link.Hubs
{
    /// <summary>
    /// 实时连接入口。协议逻辑在 <see cref="IConnectionCoordinator"/>，这里只做身份解析与转发。
    /// </summary>
    public class ChatHub : Hub
    {
        private readonly IConnectionCoordinator _coordinator;

        public ChatHub(IConnectionCoordinator coordinator)
        {
            _coordinator = coordinator;
        }

        public override async Task OnConnectedAsync()
        {
            // 身份优先取网关回填的 x-viv-* 头（RequestTokenResolver 验签 x-request-token）；
            // 直连 Link 时没有这些头，回落到 IM 自己签发的 JWT
            var httpContext = Context.GetHttpContext();
            var identity = httpContext == null ? null : RequestTokenResolver.GetContextFromHeaders(httpContext);

            if (identity == null && httpContext != null)
                identity = await RequestTokenResolver.GetContextFromTokenAsync(httpContext);

            if (identity == null || identity.AppId <= 0 || identity.SubjectId <= 0 || identity.UserId <= 0)
            {
                Context.Abort();
                return;
            }

            var connected = await _coordinator.RegisterAsync(Context.ConnectionId, identity);
            if (connected == null)
            {
                Context.Abort();
                return;
            }

            await Clients.Caller.SendAsync(HertaLinkClientMethods.Connected, connected);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await _coordinator.UnregisterAsync(Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>心跳 + 续租。客户端据此校时，并知道租约何时到期</summary>
        public Task<HertaLinkPong> Ping() => _coordinator.PingAsync(Context.ConnectionId);

        /// <summary>投递确认（本期只统计）</summary>
        public void Ack(long[] messageIds) => _coordinator.Ack(Context.ConnectionId, messageIds);

        /// <summary>重连对齐：告诉客户端要不要去 Herta.Api 补拉</summary>
        public HertaLinkResyncResult Resync(long? lastMessageId = null)
            => _coordinator.Resync(Context.ConnectionId, lastMessageId);
    }
}
