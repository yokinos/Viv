using Viv.Kinna.Connections;
using Viv.Kinna.Hubs;
using Viv.Kinna.Ports;
using Viv.Log;

namespace Viv.Herta.Link.Hubs
{
    /// <summary>
    /// 聊天实时入口。连接、租约、心跳、确认这些都在 <see cref="KinnaHubBase{THub}"/> 里，
    /// 这里只放聊天自己的上行方法（目前还没有，全部下行）。
    ///
    /// 和 <see cref="LineHub"/> 是两个独立入口：连接表、组表、配置前缀各一份，
    /// 同一个人从两个 Hub 上线是两条连接，互不影响。
    /// </summary>
    public class ChatHub : KinnaHubBase<ChatHub>
    {
        public ChatHub(
            IKinnaConnectionCoordinator<ChatHub> coordinator,
            IKinnaAdmission<ChatHub> admission,
            IKinnaIdentityResolver<ChatHub> identity,
            ILoggerContract logger)
            : base(coordinator, admission, identity, logger)
        {
        }
    }
}
