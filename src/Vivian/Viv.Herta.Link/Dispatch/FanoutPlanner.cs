using Viv.Entity.Enums;
using Viv.EventContracts.Herta;
using Viv.Herta.Link.Contracts;

namespace Viv.Herta.Link.Dispatch
{
    /// <summary>
    /// 默认寻址：单聊 → 用户键（查本机连接表）；群聊 → 群组名（投本机 SignalR 组）。
    /// </summary>
    public class FanoutPlanner : IFanoutPlanner
    {
        /// <inheritdoc />
        public FanoutDecision Plan(SendMessageEvent evt, long tenantId)
        {
            return evt.ReceiverType == EmChatReceiverType.Group
                ? new FanoutDecision(FanoutKind.Group, HertaLinkKeys.GetGroupName(tenantId, evt.TargetId))
                : new FanoutDecision(FanoutKind.User, HertaLinkKeys.GetUserKey(tenantId, evt.TargetId));
        }
    }
}
