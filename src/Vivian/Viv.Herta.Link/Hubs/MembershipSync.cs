using Viv.EventContracts.Herta;
using Viv.Kinna.Connections;
using Viv.Log;

namespace Viv.Herta.Link.Hubs
{
    /// <summary>
    /// 成员变更只作用于本进程持有的连接。入参暂时还是 Herta 自己的事件类型，泛化留到后面。
    /// </summary>
    public class MembershipSync : IMembershipSync
    {
        private readonly IKinnaSession<ChatHub> _session;
        private readonly ILoggerContract _logger;

        public MembershipSync(IKinnaSession<ChatHub> session, ILoggerContract logger)
        {
            _session = session;
            _logger = logger;
        }

        public async Task ApplyAsync(MembershipChangedEvent evt, CancellationToken cancellationToken = default)
        {
            if (evt.TenantId <= 0 || evt.GroupId <= 0 || evt.UserIds.Count == 0)
                return;

            var groupName = _session.Keys.GroupName(evt.TenantId, evt.GroupId);
            var changed = 0;

            foreach (var userId in evt.UserIds)
            {
                // 只处理本进程持有的连接，其他副本各管自己的
                var connectionIds = _session.Pool.GetConnectionIds(evt.TenantId, userId);
                foreach (var connectionId in connectionIds)
                {
                    if (evt.ChangeType == MembershipChangeType.Joined)
                        await _session.Sender.AddToGroupAsync(connectionId, groupName, cancellationToken).ConfigureAwait(false);
                    else
                        await _session.Sender.RemoveFromGroupAsync(connectionId, groupName, cancellationToken).ConfigureAwait(false);

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
