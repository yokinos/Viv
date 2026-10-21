using Viv.Entity.Enums;
using Viv.Entity.Interface;
using Viv.Delusion.Extension;
using Viv.Nana;

namespace Viv.EventContracts.Herta
{
    /// <summary>
    /// 发送消息MQ事件
    /// </summary>
    public class SendMessageEvent : NanaEvent
    {
        public SendMessageEvent() { }

        public SendMessageEvent(long fromUserId, long targetId, IHertaChatMessage message, EmChatReceiverType receiverType, EmChatMessageType messageType)
        {
            FromUserId = fromUserId;
            TargetId = targetId;
            ReceiverType = receiverType;
            MessageType = messageType;
            Message = message.ToJson();
        }

        /// <summary>
        /// 消息署名的发送者。0 表示系统消息。
        /// 与发布者是谁无关：模拟发送、机器人代发时这里可以填别人，投递按这个值送，审计另记。
        /// </summary>
        public long FromUserId { get; set; }

        /// <summary>
        /// 消息所属租户。大于 0 时以它为准，用于模拟其他租户或系统代发；
        /// 0 表示用信封上下文里的租户。
        /// </summary>
        public long TenantId { get; set; }

        /// <summary>
        /// 接收方的Id
        /// </summary>
        public long TargetId { get; set; }

        /// <summary>
        /// 落库后的消息行Id
        /// </summary>
        public long MessageId { get; set; }

        /// <summary>
        /// 落库时间。与拉取返回的 SentAt 同源，客户端排序不会因为两条通道不同而抖动
        /// </summary>
        public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// 接收方的类型
        /// </summary>
        public EmChatReceiverType ReceiverType { get; set; }

        /// <summary>
        /// 消息类型
        /// </summary>
        public EmChatMessageType MessageType { get; set; }

        /// <summary>
        /// 消息[Json]
        /// <see cref="IHertaChatMessage"/>
        /// </summary>
        public string Message { get; private set; } = string.Empty;
    }
}
