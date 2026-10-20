using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Viv.Contracts.Models;
using Viv.Herta.Core.IService;
using Viv.Herta.Link.Contracts;
using Viv.Herta.Link.Options;
using Viv.Log;

namespace Viv.Herta.Link.Hubs
{
    public class ConnectionCoordinator : IConnectionCoordinator
    {
        private readonly IConnectionPool _pool;
        private readonly IGroupService _groupService;
        private readonly IPresenceStore _presence;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly ILoggerContract _logger;
        private readonly TimeSpan _leaseTtl;

        public ConnectionCoordinator(
            IConnectionPool pool,
            IGroupService groupService,
            IPresenceStore presence,
            IHubContext<ChatHub> hubContext,
            IOptions<HertaLinkOptions> options,
            ILoggerContract logger)
        {
            _pool = pool;
            _groupService = groupService;
            _presence = presence;
            _hubContext = hubContext;
            _logger = logger;
            _leaseTtl = TimeSpan.FromSeconds(Math.Max(1, options.Value.EntryTtlSeconds));
        }

        public async Task<HertaLinkConnected?> RegisterAsync(
            string connectionId,
            VivContextContent identity,
            CancellationToken cancellationToken = default)
        {
            if (identity.SubjectId <= 0 || identity.UserId <= 0)
                return null;

            _pool.Add(connectionId, identity.AppId, identity.SubjectId, identity.UserId);
            await _presence.OnlineAsync(identity.SubjectId, identity.UserId).ConfigureAwait(false);

            // 无背板时组是本进程的，所以只有持有该连接的这个副本需要加组
            var groupIds = await _groupService.GetUserGroupIdsAsync(identity.SubjectId, identity.UserId).ConfigureAwait(false);
            foreach (var groupId in groupIds)
            {
                await _hubContext.Groups
                    .AddToGroupAsync(connectionId, HertaLinkKeys.GetGroupName(identity.SubjectId, groupId), cancellationToken)
                    .ConfigureAwait(false);
            }

            return new HertaLinkConnected
            {
                ConnectionId = connectionId,
                ServerTime = DateTimeOffset.UtcNow
            };
        }

        public async Task UnregisterAsync(string connectionId, CancellationToken cancellationToken = default)
        {
            // 先取身份再移除，移除后就查不到了
            var info = _pool.TryGet(connectionId);
            _pool.Remove(connectionId);

            if (info != null)
                await _presence.OfflineAsync(info.Value.SubjectId, info.Value.UserId).ConfigureAwait(false);
        }

        public async Task<HertaLinkPong> PingAsync(string connectionId, CancellationToken cancellationToken = default)
        {
            var info = _pool.TryGet(connectionId);
            _pool.Touch(connectionId);

            if (info != null)
                await _presence.RenewAsync(info.Value.SubjectId, info.Value.UserId).ConfigureAwait(false);

            var now = DateTimeOffset.UtcNow;
            return new HertaLinkPong
            {
                ServerTime = now,
                LeaseExpiresAt = now + _leaseTtl
            };
        }

        public void Ack(string connectionId, long[] messageIds)
        {
            // 投递确认只做统计，落库的已读回执属于 Herta.Core
            if (messageIds.Length == 0) return;

            _logger.Debug($"收到投递确认 {messageIds.Length} 条: connection={connectionId}");
        }

        public HertaLinkResyncResult Resync(string connectionId, long? lastMessageId)
        {
            // Link 不存消息，只能告诉客户端「去 Herta.Api 补」，不能替它补
            _logger.Debug($"重连对齐: connection={connectionId} lastMessageId={lastMessageId}");

            return new HertaLinkResyncResult { NeedBackfill = true };
        }
    }
}
