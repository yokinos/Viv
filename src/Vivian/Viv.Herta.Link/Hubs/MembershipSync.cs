using Microsoft.AspNetCore.SignalR;
using Viv.EventContracts.Herta;
using Viv.Herta.Link.Contracts;
using Viv.Log;

namespace Viv.Herta.Link.Hubs
{
    public class MembershipSync : IMembershipSync
    {
        private readonly IConnectionPool _pool;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly ILoggerContract _logger;

        public MembershipSync(IConnectionPool pool, IHubContext<ChatHub> hubContext, ILoggerContract logger)
        {
            _pool = pool;
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task ApplyAsync(MembershipChangedEvent evt, CancellationToken cancellationToken = default)
        {
            if (evt.TenantId <= 0 || evt.GroupId <= 0 || evt.UserIds.Count == 0)
                return;

            var groupName = HertaLinkKeys.GetGroupName(evt.TenantId, evt.GroupId);
            var changed = 0;

            foreach (var userId in evt.UserIds)
            {
                // 只处理本进程持有的连接，其他副本各管自己的
                var connectionIds = _pool.GetConnectionIds(evt.TenantId, userId);
                foreach (var connectionId in connectionIds)
                {
                    if (evt.ChangeType == MembershipChangeType.Joined)
                        await _hubContext.Groups.AddToGroupAsync(connectionId, groupName, cancellationToken).ConfigureAwait(false);
                    else
                        await _hubContext.Groups.RemoveFromGroupAsync(connectionId, groupName, cancellationToken).ConfigureAwait(false);

                    changed++;
                }
            }

            if (changed > 0)
            {
                _logger.Info($"群 {evt.GroupId} 成员变更 {evt.ChangeType}，本实例更新 {changed} 条连接");
            }
        }
    }
}
