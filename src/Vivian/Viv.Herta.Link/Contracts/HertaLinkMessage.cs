using Viv.Entity.Enums;
using Viv.Kinna.Contracts;

namespace Viv.Herta.Link.Contracts
{
    /// <summary>下发客户端的消息体，ReceiveMessage 的载荷。版本字段 V 继承自 KinnaPayload。</summary>
    public sealed record HertaLinkMessage : KinnaPayload
    {
        /// <summary>落库后的消息行 Id。客户端的幂等键，也是拉取接口的游标</summary>
        public long MessageId { get; init; }

        /// <summary>会话内自增序号。本期恒 null：它要在落库时分配 </summary>
        public long? Seq { get; init; }

        public long TenantId { get; init; }

        public long? AppId { get; init; }

        /// <summary>0 = 系统消息</summary>
        public long FromUserId { get; init; }

        /// <summary>单聊为对方用户Id；群聊恒为 0，群标识见 <see cref="GroupId"/></summary>
        public long ToUserId { get; init; }

        public long? GroupId { get; init; }

        public EmChatReceiverType ReceiverType { get; init; }

        public EmChatMessageType MessageType { get; init; }

        /// <summary>
        /// 声明成 object 而不是接口：System.Text.Json 按声明类型选契约，
        /// 接口只会序列化 MessageType，正文整个丢掉。
        /// </summary>
        public object? Body { get; init; }

        /// <summary>落库时间，与拉取接口返回的 SentAt 同源</summary>
        public DateTimeOffset SentAt { get; init; }

        public string? TraceId { get; init; }
    }
}
