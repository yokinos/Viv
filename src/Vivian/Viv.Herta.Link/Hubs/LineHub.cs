using Viv.Kinna.Connections;
using Viv.Kinna.Hubs;
using Viv.Kinna.Ports;
using Viv.Log;

namespace Viv.Herta.Link.Hubs
{
    /// <summary>
    /// 第二个实时入口。连接、租约、心跳、确认和 <see cref="ChatHub"/> 是同一套逻辑
    /// （都在 <see cref="KinnaHubBase{THub}"/> 里），但它有自己的连接表、组表、租约清扫
    /// 和配置段（<c>VivOptions:KinnaOption:LineHub</c>），两侧的在线态互不可见。
    ///
    /// 上行方法还没有，先提供接入能力；业务方法直接加在这个类上即可，
    /// 推送侧加一个 <c>IKinnaPusher&lt;LineHub, TEvent&gt;</c> 就会被
    /// <c>AddKinnaPusher&lt;LineHub&gt;()</c> 自动扫到。
    /// </summary>
    public sealed class LineHub : KinnaHubBase<LineHub>
    {
        public LineHub(
            IKinnaConnectionCoordinator<LineHub> coordinator,
            IKinnaAdmission<LineHub> admission,
            IKinnaIdentityResolver<LineHub> identity,
            ILoggerContract logger)
            : base(coordinator, admission, identity, logger)
        {
        }
    }
}
