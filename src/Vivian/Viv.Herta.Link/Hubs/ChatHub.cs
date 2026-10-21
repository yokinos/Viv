using Microsoft.AspNetCore.SignalR;
using Viv.Engine.Power;
using Viv.Kinna.Connections;
using Viv.Kinna.Contracts;
using Viv.Log;

namespace Viv.Herta.Link.Hubs
{
    /// <summary>
    /// 实时连接入口。身份解析留在这里，因为走网关和直连是两套取法；其余动作都转给协调器。
    /// </summary>
    public class ChatHub : Hub
    {
        private readonly IKinnaConnectionCoordinator<ChatHub> _coordinator;
        private readonly HertaLinkAdmission _admission;
        private readonly ILoggerContract _logger;

        public ChatHub(
            IKinnaConnectionCoordinator<ChatHub> coordinator,
            HertaLinkAdmission admission,
            ILoggerContract logger)
        {
            _coordinator = coordinator;
            _admission = admission;
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            // 身份优先取网关回填的 x-viv-* 头（RequestTokenResolver 验签 x-request-token）；
            // 直连 Link 时没有这些头，回落到 IM 自己签发的 JWT
            var httpContext = Context.GetHttpContext();
            var identity = httpContext == null ? null : RequestTokenResolver.GetContextFromHeaders(httpContext);

            if (identity == null && httpContext != null)
                identity = await RequestTokenResolver.GetContextFromTokenAsync(httpContext);

            var check = await _admission.CheckAsync(identity, Context.ConnectionAborted);
            if (!check.Allowed)
            {
                _logger.Warning($"拒绝建立连接: {check.Reason} connection={Context.ConnectionId}");
                Context.Abort();
                return;
            }

            var connected = await _coordinator.RegisterAsync(Context.ConnectionId, identity!);
            if (connected == null)
            {
                Context.Abort();
                return;
            }

            await Clients.Caller.SendAsync(KinnaClientMethods.Connected, connected);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await _coordinator.UnregisterAsync(Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>心跳 + 续租。客户端据此校时，并知道租约何时到期</summary>
        public Task<KinnaPong> Ping() => _coordinator.PingAsync(Context.ConnectionId);

        /// <summary>投递确认。客户端收到消息后上报，服务端记已确认水位，返回当前水位</summary>
        public Task<long> Ack(long[] messageIds) => _coordinator.AckAsync(Context.ConnectionId, messageIds);

        /// <summary>重连对齐。返回服务端水位，NeedBackfill 为 true 时客户端走 HTTP 增量拉取</summary>
        public Task<KinnaResyncResult> Resync(long? lastMessageId = null)
            => _coordinator.ResyncAsync(Context.ConnectionId, lastMessageId);
    }
}
