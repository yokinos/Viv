using Viv.EventContracts.Herta;

namespace Viv.Herta.Link.Hubs
{
    public interface IMembershipSync
    {
        /// <summary>
        /// 按变更事件更新本进程连接所在的 SignalR 组
        /// </summary>
        Task ApplyAsync(MembershipChangedEvent evt, CancellationToken cancellationToken = default);
    }
}
