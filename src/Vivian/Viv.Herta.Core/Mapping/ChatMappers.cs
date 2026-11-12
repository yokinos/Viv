using Riok.Mapperly.Abstractions;
using Viv.Herta.Core.Entity.Model.Chat;
using Viv.Herta.Core.Entity.Vo.Chat;

namespace Viv.Herta.Core.Mapping
{
    /// <summary>
    /// 消息模型的映射。字段一一对应，时间字段两侧都是 <see cref="DateTimeOffset"/>，直接对拷。
    /// 输出 DTO 不带聊天域和接收方类型 —— 寻址信息在请求里，客户端不需要每条消息再回一份。
    /// </summary>
    [Mapper]
    public static partial class ChatMappers
    {
        [MapperIgnoreSource(nameof(ChatMessageEntry.RealmId))]
        [MapperIgnoreSource(nameof(ChatMessageEntry.ReceiverType))]
        public static partial MessageItemOutput ToItem(ChatMessageEntry entry);
    }
}
