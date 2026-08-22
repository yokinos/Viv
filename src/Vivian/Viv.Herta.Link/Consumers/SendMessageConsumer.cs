using Microsoft.AspNetCore.SignalR;
using Viv.Entity.Enums;
using Viv.EventContracts.Herta;
using Viv.Herta.Core.Entity.Message;
using Viv.Herta.Core.Magic;
using Viv.Herta.Link.Hubs;
using Viv.Nana;

namespace Viv.Herta.Link.Consumers
{
    public class SendMessageConsumer : VivConsumer<SendMessageEvent>
    {
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly IConnectionPool _connectionPool;

        public SendMessageConsumer(VivConsumerDependency dependency, IHubContext<ChatHub> hubContext, IConnectionPool connectionPool)
            : base(dependency)
        {
            _hubContext = hubContext;
            _connectionPool = connectionPool;
        }

        public override async Task<SubscribeResult> ReceiveMessageAsync(NanaEnvelope<SendMessageEvent> envelope, CancellationToken cancellationToken = default)
        {
            var evt = envelope.Content;
            if (evt == null)
                return new SubscribeResult(false, false, "Message content is null");

            var body = HertaMagic.GetChatMessage(evt.MessageType, evt.Message);

            var chatMessage = new HertaChatMessage
            {
                Id = envelope.MessageId,
                AppId = envelope.Context?.AppId,
                FromUserId = evt.FromUserId,
                ToUserId = evt.TargetId,
                Body = body,
                SentAt = DateTimeOffset.UtcNow
            };

            var tenantId = envelope.Context?.SubjectId ?? 0;
            if (evt.ReceiverType == EmChatReceiverType.Group)
            {
                var groupName = HertaLinkGroups.GetGroupName(tenantId, evt.TargetId);
                await _hubContext.Clients.Group(groupName).SendAsync(HertaLinkClientMethods.ReceiveMessage, chatMessage, cancellationToken);
            }
            else
            {
                var connectionIds = _connectionPool.GetConnectionIds(tenantId, evt.TargetId);
                if (connectionIds.Count > 0)
                {
                    await _hubContext.Clients.Clients(connectionIds).SendAsync(HertaLinkClientMethods.ReceiveMessage, chatMessage, cancellationToken);
                }
            }

            return new SubscribeResult(true, false, "OK");
        }
    }
}
