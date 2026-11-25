using Microsoft.AspNetCore.SignalR;
using Viv.Kinna.Connections;
using Viv.Kinna.Contracts;
using Viv.Kinna.Ports;
using Viv.Log;

namespace Viv.Kinna.Hubs
{
    /// <summary>
    /// SignalR Hub 的连接生命周期：解析身份 → 准入 → 登记，断连时反登记；上行心跳、
    /// 投递确认、重连对齐也在这里。业务方法留在各自的 Hub 上。
    ///
    /// 放在框架里而不是某个接入方里：这里每一行都是线上协议（方法名、帧形状、
    /// 「表里没这条连接就回完 Pong 再断」），不是业务约定。协议改了这里必须跟着改，
    /// 分散到各接入方就只会改一边。
    ///
    /// 身份走 <see cref="IKinnaIdentityResolver{THub}"/>、准入走 <see cref="IKinnaAdmission{THub}"/>，
    /// 都是端口而不是具体实现：原生 WebSocket 端点用的是同一对，两套传输共用一张连接表，
    /// 就不能各有一套放行标准。
    /// </summary>
    /// <typeparam name="THub">具体 Hub 自身，用来闭合各端口的泛型</typeparam>
    public abstract class KinnaHubBase<THub> : Hub where THub : Hub
    {
        private readonly IKinnaConnectionCoordinator<THub> _coordinator;
        private readonly IKinnaAdmission<THub> _admission;
        private readonly IKinnaIdentityResolver<THub> _identity;
        private readonly ILoggerContract _logger;

        protected KinnaHubBase(
            IKinnaConnectionCoordinator<THub> coordinator,
            IKinnaAdmission<THub> admission,
            IKinnaIdentityResolver<THub> identity,
            ILoggerContract logger)
        {
            _coordinator = coordinator;
            _admission = admission;
            _identity = identity;
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            // 身份解析放在解析器里而不是内联：原生端点和 Hub 走同一份实现，
            // 只改一边就会出现「换传输换身份判定」
            var httpContext = Context.GetHttpContext();
            var identity = httpContext == null ? null : await _identity.ResolveAsync(httpContext);

            var check = await _admission.CheckAsync(identity, Context.ConnectionAborted);
            if (!check.Allowed)
            {
                _logger.Warning($"拒绝建立连接: {check.Reason} connection={Context.ConnectionId}");
                Context.Abort();
                return;
            }

            // 握手帧在加组之前由回调发出：加组一完成群推送就可能到达，
            // 等 RegisterAsync 返回再发，客户端会先收到业务帧再收到 Connected
            var connected = await _coordinator.RegisterAsync(
                Context.ConnectionId,
                identity!,
                Context.ConnectionAborted,
                frame => Clients.Caller.SendAsync(KinnaClientMethods.Connected, frame));

            if (connected == null)
            {
                Context.Abort();
                return;
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await _coordinator.UnregisterAsync(Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// 心跳 + 续租。客户端据此校时，并知道租约何时到期。
        ///
        /// 表里已经没有这条连接时（超租约被清扫、被别的副本踢掉），回完 Pong 再硬断：
        /// 和原生端点保持同一份行为。只回 <c>Alive = false</c> 却不动连接的话，
        /// 客户端只要不看这个字段，就会一直挂在一条收不到任何消息的连接上。
        /// </summary>
        public async Task<KinnaPong> Ping()
        {
            var pong = await _coordinator.PingAsync(Context.ConnectionId);
            if (pong.Alive)
            {
                return pong;
            }

            try
            {
                // 先按既有约定软踢一次：客户端据此知道「是我该走」，而不是网络抖了一下。
                // 用 SendCoreAsync 传空参数数组，和协调器踢人时的载荷形状一致
                await Clients.Caller.SendCoreAsync(KinnaClientMethods.ForceDisconnect, [], Context.ConnectionAborted);
            }
            catch (Exception)
            {
                // 对端已经走了；紧接着的 Abort 就是收尾
            }

            Context.Abort();
            return pong;
        }

        /// <summary>投递确认。客户端收到消息后上报，服务端记已确认水位；返回结果区分限流/未知连接/落库失败</summary>
        public Task<KinnaAckResult> Ack(long[] messageIds) => _coordinator.AckAsync(Context.ConnectionId, messageIds);

        /// <summary>重连对齐。返回服务端水位，NeedBackfill 为 true 时客户端走 HTTP 增量拉取</summary>
        public Task<KinnaResyncResult> Resync(long? lastMessageId = null)
            => _coordinator.ResyncAsync(Context.ConnectionId, lastMessageId);
    }
}
