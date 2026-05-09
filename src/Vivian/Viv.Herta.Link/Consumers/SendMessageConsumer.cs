using Microsoft.AspNetCore.SignalR;
using Viv.Entity.Chat;
using Viv.Entity.Enums;
using Viv.EventContracts.Herta;
using Viv.Herta.Core.Magic;
using Viv.Herta.Link.Hubs;
using Viv.Log;
using Viv.Nana;
using Viv.Nana.Models;

namespace Viv.Herta.Link.Consumers
{
    public class SendMessageConsumer : VivConsumer<SendMessageEvent>
    {
        private readonly IHubContext<ChatHub> _hubContext;

        public SendMessageConsumer(IDistributedLogger logger, IHubContext<ChatHub> hubContext)
            : base(logger)
        {
            _hubContext = hubContext;
        }

        public override async Task<SubscribeResult> ReceiveMessageAsync(NanaMessage<SendMessageEvent> message, CancellationToken cancellationToken = default)
        {
            var evt = message.Content;
            if (evt == null)
                return new SubscribeResult(false, false, "Message content is null");

            var body = HertaMagic.GetChatMessage(evt.MessageType, evt.Message);

            var chatMessage = new ChatMessage
            {
                Id = message.MessageId,
                AppId = message.AppId,
                FromUserId = evt.FromUserId,
                ToUserId = evt.TargetId,
                Body = body,
                SentAt = DateTimeOffset.UtcNow
            };

            if (evt.ReceiverType == EmChatReceiverType.Group)
            {
                var groupName = ChatHub.GetGroupName(message.TenantId, evt.TargetId);
                await _hubContext.Clients.Group(groupName).SendAsync("ReceiveMessage", chatMessage, cancellationToken);
            }
            else
            {
                var connectionIds = ConnectionPool.GetConnectionIds(message.TenantId, evt.TargetId);
                if (connectionIds.Count > 0)
                {
                    await _hubContext.Clients.Clients(connectionIds).SendAsync("ReceiveMessage", chatMessage, cancellationToken);
                }
            }

            return new SubscribeResult(true, false, "OK");
        }
    }
}
