using System;
using System.Collections.Generic;
using System.Text;
using Viv.Engine;
using Viv.EventContracts.Herta;
using Viv.Herta.Core.Entity.Dto.Chat;
using Viv.Herta.Core.IService;
using Viv.Herta.Core.Magic;
using Viv.Nana;
using Viv.Vva.Extension;

namespace Viv.Herta.Core.Service
{
    public class ChatService : IChatService
    {
        private readonly IVivPublisher _vivPublisher;

        public ChatService(IVivPublisher vivPublisher)
        {
            _vivPublisher = vivPublisher;
        }

        public async Task<VivApiResult> SendMessageAsync(SendMessageRequest request)
        {
            var messaage = HertaMagic.GetChatMessage(request.MessageType, request.Message);
            if (messaage == null)
            {
                return VivApiResult.Error("消息错误");
            }

            var sendMessageEvent = new SendMessageEvent(request.FromUserId, request.TargetId, messaage, request.ReceiverType, request.MessageType);
            await _vivPublisher.PublishAsync(sendMessageEvent);

            return VivApiResult.Success();
        }
    }
}
