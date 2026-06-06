using Viv.Entity.Enums;
using Viv.Entity.Interface;
using Viv.Nana.Core;
using Viv.Vva.Extension;

namespace Viv.EventContracts.Herta
{
    /// <summary>
    /// 发送消息MQ事件
    /// </summary>
    public class SendMessageEvent : NanaEvent
    {
        public SendMessageEvent() { }

        public SendMessageEvent(long fromUserId, long targetId, IChatMessage message, EmChatReceiverType receiverType, EmChatMessageType messageType)
        {
            FromUserId = fromUserId;
            TargetId = targetId;
            ReceiverType = receiverType;
            MessageType = messageType;
            Message = message.ToJson();
        }

        /// <summary>
        /// 发送方的Id 如果为0则为系统消息
        /// </summary>
        public long FromUserId { get; set; }

        /// <summary>
        /// 接收方的Id
        /// </summary>
        public long TargetId { get; set; }

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
        /// <see cref="IChatMessage"/>
        /// </summary>
        public string Message { get; private set; }
    }
}
