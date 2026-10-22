using Riok.Mapperly.Abstractions;
using Viv.Herta.Core.Entity.Model.Chat;
using Viv.Herta.Core.Entity.Vo.Chat;

namespace Viv.Herta.Core.Mapping
{
    /// <summary>
    /// 消息模型的映射。字段一一对应，只有时间要特殊处理（见 <see cref="UtcTime"/>）。
    /// 输出 DTO 不带租户和接收方类型 —— 寻址信息在请求里，客户端不需要每条消息再回一份。
    /// </summary>
    [Mapper]
    public static partial class ChatMappers
    {
        [MapperIgnoreSource(nameof(ChatMessageEntry.TenantId))]
        [MapperIgnoreSource(nameof(ChatMessageEntry.ReceiverType))]
        public static partial MessageItemOutput ToItem(ChatMessageEntry entry);

        private static DateTimeOffset ToOffset(DateTime value) => UtcTime.FromUtc(value);

        private static DateTimeOffset? ToOffset(DateTime? value) => UtcTime.FromUtc(value);
    }
}
