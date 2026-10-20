using Viv.Contracts.Models;
using Viv.Herta.Link.Contracts;

namespace Viv.Herta.Link.Hubs
{
    /// <summary>
    /// 连接生命周期与协议应答。放这里而不是 Hub 里，是因为 Hub 依赖 Hub.Context，没有夹具无法单测。
    /// </summary>
    public interface IConnectionCoordinator
    {
        /// <summary>登记连接并加入所属群组；返回 null 表示该身份不可用</summary>
        Task<HertaLinkConnected?> RegisterAsync(string connectionId, VivContextContent identity, CancellationToken cancellationToken = default);

        Task UnregisterAsync(string connectionId, CancellationToken cancellationToken = default);

        Task<HertaLinkPong> PingAsync(string connectionId, CancellationToken cancellationToken = default);

        void Ack(string connectionId, long[] messageIds);

        HertaLinkResyncResult Resync(string connectionId, long? lastMessageId);
    }
}
