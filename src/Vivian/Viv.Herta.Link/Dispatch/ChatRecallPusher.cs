using Viv.Entity.Enums;
using Viv.EventContracts.Herta;
using Viv.Herta.Link.Contracts;
using Viv.Herta.Link.Hubs;
using Viv.Kinna.Connections;
using Viv.Kinna.Contracts;
using Viv.Kinna.Dispatch;

namespace Viv.Herta.Link.Dispatch
{
    /// <summary>把撤回事件翻译成推送计划。</summary>
    public sealed class ChatRecallPusher : IKinnaPusher<MessageRecalledEvent>
    {
        private readonly IKinnaSession<ChatHub> _session;

        public ChatRecallPusher(IKinnaSession<ChatHub> session)
        {
            _session = session;
        }

        public KinnaPushPlan? Plan(MessageRecalledEvent evt, KinnaPushContext? context)
        {
            var subjectId = evt.TenantId > 0 ? evt.TenantId : context?.SubjectId ?? 0;
            if (subjectId <= 0 || evt.TargetId <= 0)
                return null;

            var payload = new HertaLinkRecall
            {
                MessageId = evt.MessageId,
                RecalledAt = evt.RecalledAt
            };

            return evt.ReceiverType == EmChatReceiverType.Group
                ? new KinnaPushPlan(
                    "group",
                    KinnaClientMethods.MessageRecalled,
                    payload,
                    KinnaPushTarget.ToGroup(_session.Keys.GroupName(subjectId, evt.TargetId)))
                : new KinnaPushPlan(
                    "user",
                    KinnaClientMethods.MessageRecalled,
                    payload,
                    KinnaPushTarget.ToUser(subjectId, evt.TargetId));
        }
    }
}
