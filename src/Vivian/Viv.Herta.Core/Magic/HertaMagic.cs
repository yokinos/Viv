using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Viv.Entity.Chat;
using Viv.Entity.Chat.Message;
using Viv.Entity.Enums;
using Viv.Vva.Extension;

namespace Viv.Herta.Core.Magic
{
    public class HertaMagic
    {
        [return:MaybeNull]
        public static IChatMessage GetChatMessage(EmChatMessageType messageType, string json)
        {
            return messageType switch
            {
                EmChatMessageType.Text => json.As<TextMessage>(),
                EmChatMessageType.MediaFile => json.As<MediaFileMessage>(),
                EmChatMessageType.Mix => json.As<MixMessage>(),
                EmChatMessageType.Command => json.As<CommandMessage>(),
                _ => default
            };
        }
    }
}
