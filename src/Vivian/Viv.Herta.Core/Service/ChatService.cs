using Viv.Contracts.Attributes;
using Viv.Engine;
using Viv.EventContracts.Herta;
using Viv.Herta.Core.Entity.Dto.Chat;
using Viv.Herta.Core.IService;
using Viv.Herta.Core.Magic;
using Viv.Outbox;
using Viv.Delusion.Extension;

namespace Viv.Herta.Core.Service
{
    public class ChatService : IChatService
    {
        private readonly IVivOutbox _outbox;

        public ChatService(IVivOutbox outbox)
        {
            _outbox = outbox;
        }

        /// <summary>
        /// 窄写入样本：消息入队走发件箱，与 <c>[VivUnitOfWork]</c> 同事务。
        /// 业务失败信封或异常会回滚待发行，投递器看不到这条。
        /// </summary>
        [VivUnitOfWork]
        public virtual async Task<VivApiResult> SendMessageAsync(SendMessageRequest request)
        {
            var messaage = HertaMagic.GetChatMessage(request.MessageType, request.Message);
            if (messaage == null)
            {
                return VivApiResult.Failed("消息错误");
            }

            var sendMessageEvent = new SendMessageEvent(request.FromUserId, request.TargetId, messaage, request.ReceiverType, request.MessageType);
            await _outbox.EnqueueAsync(sendMessageEvent);

            return VivApiResult.Success();
        }
    }
}
