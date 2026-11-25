using Viv.Entity.Enums;
using Viv.EventContracts.Herta;
using Viv.Herta.Link.Contracts;
using Viv.Herta.Link.Hubs;
using Viv.Kinna.Connections;
using Viv.Kinna.Contracts;
using Viv.Kinna.Dispatch;

namespace Viv.Herta.Link.Dispatch
{
    /// <summary>
    /// 把撤回事件翻译成推送计划。
    /// </summary>
    public sealed class ChatRecallPusher : IKinnaPusher<ChatHub, MessageRecalledEvent>
    {
        private readonly IKinnaSession<ChatHub> _session;

        public ChatRecallPusher(IKinnaSession<ChatHub> session)
        {
            _session = session;
        }

        public KinnaPushPlan? Plan(MessageRecalledEvent evt, KinnaPushContext? context)
        {
            var subjectId = evt.RealmId > 0 ? evt.RealmId : context?.SubjectId ?? 0;
            if (subjectId <= 0 || evt.TargetId <= 0)
                return null;

            var payload = new HertaLinkRecall
            {
                MessageId = evt.MessageId,
                RecalledAt = evt.RecalledAt
            };

            // 撤回同上：群里靠组播覆盖发送方其它端，单聊要显式补投。
            // 事件里没有署名，补投目标由投递流程用信封的用户兜底（撤回是自己撤自己发的）
            return evt.ReceiverType == EmChatReceiverType.Group
                ? new KinnaPushPlan(
                    "group",
                    KinnaClientMethods.MessageRecalled,
                    payload,
                    KinnaPushTarget.ToGroup(subjectId, evt.TargetId))
                : new KinnaPushPlan(
                    "user",
                    KinnaClientMethods.MessageRecalled,
                    payload,
                    KinnaPushTarget.ToUser(subjectId, evt.TargetId),
                    MirrorToSender: true);
        }
    }
}
