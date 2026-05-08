using Microsoft.AspNetCore.SignalR;
using Viv.Herta.Core.Events;
using Viv.Herta.Core.Models;
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

            var chatMessage = new ChatMessage
            {
                MessageId = evt.MessageId,
                FromUserId = evt.FromUserId,
                ToUserId = evt.ToUserId,
                Content = evt.Content,
                ContentType = evt.ContentType,
                MediaInfo = evt.MediaInfo,
                Segments = evt.Segments,
                SentAt = DateTimeOffset.UtcNow
            };

            var connectionIds = ConnectionPool.GetConnectionIds(message.TenantId, evt.ToUserId);

            if (connectionIds.Count > 0)
            {
                await _hubContext.Clients.Clients(connectionIds)
                    .SendAsync("ReceiveMessage", chatMessage, cancellationToken);
            }

            return new SubscribeResult(true, false, "OK");
        }
    }
}
