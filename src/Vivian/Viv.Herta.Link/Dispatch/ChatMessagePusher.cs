using Viv.Entity.Enums;
using Viv.EventContracts.Herta;
using Viv.Herta.Core.Magic;
using Viv.Herta.Link.Contracts;
using Viv.Herta.Link.Hubs;
using Viv.Kinna.Connections;
using Viv.Kinna.Contracts;
using Viv.Kinna.Dispatch;

namespace Viv.Herta.Link.Dispatch
{
    /// <summary>
    /// 把聊天消息事件翻译成推送计划：载荷、方法名、目标。
    /// </summary>
    public sealed class ChatMessagePusher : IKinnaPusher<ChatHub, SendMessageEvent>
    {
        private readonly IKinnaSession<ChatHub> _session;

        public ChatMessagePusher(IKinnaSession<ChatHub> session)
        {
            _session = session;
        }

        public KinnaPushPlan? Plan(SendMessageEvent evt, KinnaPushContext? context)
        {
            // 事件体指定了域就以它为准（代发/模拟），否则用信封的
            var subjectId = evt.RealmId > 0 ? evt.RealmId : context?.SubjectId ?? 0;
            if (subjectId <= 0 || evt.TargetId <= 0)
                return null;

            var isGroup = evt.ReceiverType == EmChatReceiverType.Group;

            var payload = new HertaLinkMessage
            {
                // 事件体没有业务消息 Id 时用信封的兜底
                MessageId = evt.MessageId > 0 ? evt.MessageId : context?.TransportMessageId ?? 0,
                SentAt = evt.SentAt,
                TenantId = subjectId,
                AppId = context?.AppId > 0 ? context.AppId : null,
                FromUserId = evt.FromUserId,
                ToUserId = isGroup ? 0 : evt.TargetId,
                GroupId = isGroup ? evt.TargetId : null,
                ReceiverType = evt.ReceiverType,
                MessageType = evt.MessageType,
                Body = HertaMagic.GetChatMessage(evt.MessageType, evt.Message),
                TraceId = context?.TraceId
            };

            // 群聊：发送方的其它端本来就在组里，这次组播就同步到了。
            // 单聊：目标只有接收方，必须显式补投给发送方的其它端，否则手机发出、PC 上看不到
            return isGroup
                ? new KinnaPushPlan(
                    "group",
                    KinnaClientMethods.ReceiveMessage,
                    payload,
                    KinnaPushTarget.ToGroup(subjectId, evt.TargetId),
                    evt.FromUserId)
                : new KinnaPushPlan(
                    "user",
                    KinnaClientMethods.ReceiveMessage,
                    payload,
                    KinnaPushTarget.ToUser(subjectId, evt.TargetId),
                    evt.FromUserId,
                    MirrorToSender: true);
        }
    }
}
