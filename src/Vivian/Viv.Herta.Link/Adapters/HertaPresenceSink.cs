using Viv.Herta.Core.IService;
using Viv.Herta.Link.Hubs;
using Viv.Kinna.Ports;

namespace Viv.Herta.Link.Adapters
{
    /// <summary>
    /// 把 Herta 的在线态存储接到 Kinna 的端口上。在线态不参与投递，只给多端在线查询用。
    /// </summary>
    public sealed class HertaPresenceSink : IKinnaPresenceSink<ChatHub>
    {
        private readonly IPresenceStore _presence;

        public HertaPresenceSink(IPresenceStore presence)
        {
            _presence = presence;
        }

        public Task OnlineAsync(long subjectId, long userId) => _presence.OnlineAsync(subjectId, userId);

        public Task OfflineAsync(long subjectId, long userId) => _presence.OfflineAsync(subjectId, userId);

        public Task RenewAsync(long subjectId, long userId) => _presence.RenewAsync(subjectId, userId);
    }
}
