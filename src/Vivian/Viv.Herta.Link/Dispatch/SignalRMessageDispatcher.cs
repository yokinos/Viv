using Microsoft.AspNetCore.SignalR;
using Viv.Contracts.Models;
using Viv.Entity.Enums;
using Viv.EventContracts.Herta;
using Viv.Herta.Core.Magic;
using Viv.Herta.Link.Contracts;
using Viv.Herta.Link.Hubs;
using Viv.Herta.Link.Metrics;
using Viv.Log;
using Viv.Nana;

namespace Viv.Herta.Link.Dispatch
{
    /// <summary>
    /// 经 SignalR 推给本进程的连接。每个副本都收到全量消息，所以本进程没有就说明人不在这台机器上。
    /// </summary>
    public class SignalRMessageDispatcher : IMessageDispatcher
    {
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly IConnectionPool _connectionPool;
        private readonly IFanoutPlanner _planner;
        private readonly ILoggerContract _logger;

        public SignalRMessageDispatcher(
            IHubContext<ChatHub> hubContext,
            IConnectionPool connectionPool,
            IFanoutPlanner planner,
            ILoggerContract logger)
        {
            _hubContext = hubContext;
            _connectionPool = connectionPool;
            _planner = planner;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<SubscribeResult> DispatchAsync(NanaEnvelope<SendMessageEvent> envelope, SendMessageEvent evt, CancellationToken cancellationToken = default)
        {
            // 信封是身份的唯一真相，事件体只能与它一致
            var context = envelope.Context;
            if (context == null || context.SubjectId <= 0 || context.UserId <= 0)
                return SubscribeResult.Failed(false, "缺少可信上下文");

            // 防冒充是纵深防御，真正的修复在 ChatService（发送者只取 IVivContext.UserId）
            if (evt.FromUserId != 0 && evt.FromUserId != context.UserId)
            {
                _logger.Error($"发送者身份与信封不符，消息已丢弃: evt={evt.FromUserId} ctx={context.UserId} MessageId={envelope.MessageId}");
                HertaLinkMetrics.RecordIdentityMismatch();
                return SubscribeResult.Failed(false, "发送者身份不符");
            }

            var message = BuildMessage(envelope, evt, context);
            var decision = _planner.Plan(evt, context.SubjectId);

            try
            {
                if (decision.Kind == FanoutKind.Group)
                {
                    await _hubContext.Clients.Group(decision.Target).SendAsync(HertaLinkClientMethods.ReceiveMessage, message, cancellationToken).ConfigureAwait(false);
                    HertaLinkMetrics.RecordDispatch("group", "ok");
                    return SubscribeResult.Success();
                }

                var connectionIds = _connectionPool.GetConnectionIds(context.SubjectId, evt.TargetId);
                if (connectionIds.Count == 0)
                {
                    // 对方不在本机不是失败：离线消息由客户端重连后向 Herta.Api 拉取
                    _logger.Debug($"目标不在本实例，跳过: Tenant={context.SubjectId} To={evt.TargetId} MessageId={envelope.MessageId}");
                    HertaLinkMetrics.RecordDispatch("user", "skipped");
                    return SubscribeResult.Success();
                }

                await _hubContext.Clients.Clients(connectionIds)
                    .SendAsync(HertaLinkClientMethods.ReceiveMessage, message, cancellationToken)
                    .ConfigureAwait(false);

                HertaLinkMetrics.RecordDispatch("user", "ok");
                return SubscribeResult.Success();
            }
            catch (Exception ex)
            {
                _logger.Error($"消息推送失败: MessageId={envelope.MessageId}", ex);
                HertaLinkMetrics.RecordDispatch("user", "failed");
                return SubscribeResult.Failed(false, "推送失败");
            }
        }

        public async Task<SubscribeResult> DispatchRecallAsync(
            NanaEnvelope<MessageRecalledEvent> envelope,
            MessageRecalledEvent evt,
            CancellationToken cancellationToken = default)
        {
            if (evt.TenantId <= 0 || evt.TargetId <= 0)
                return SubscribeResult.Failed(false, "缺少租户或对端");

            var payload = new HertaLinkRecall { MessageId = evt.MessageId, RecalledAt = evt.RecalledAt };

            try
            {
                if (evt.ReceiverType == EmChatReceiverType.Group)
                {
                    await _hubContext.Clients.Group(HertaLinkKeys.GetGroupName(evt.TenantId, evt.TargetId))
                        .SendAsync(HertaLinkClientMethods.MessageRecalled, payload, cancellationToken)
                        .ConfigureAwait(false);

                    HertaLinkMetrics.RecordDispatch("group", "ok");
                    return SubscribeResult.Success();
                }

                var connectionIds = _connectionPool.GetConnectionIds(evt.TenantId, evt.TargetId);
                if (connectionIds.Count == 0)
                {
                    HertaLinkMetrics.RecordDispatch("user", "skipped");
                    return SubscribeResult.Success();
                }

                await _hubContext.Clients.Clients(connectionIds)
                    .SendAsync(HertaLinkClientMethods.MessageRecalled, payload, cancellationToken)
                    .ConfigureAwait(false);

                HertaLinkMetrics.RecordDispatch("user", "ok");
                return SubscribeResult.Success();
            }
            catch (Exception ex)
            {
                _logger.Error($"撤回推送失败: MessageId={evt.MessageId}", ex);
                HertaLinkMetrics.RecordDispatch("recall", "failed");
                return SubscribeResult.Failed(false, "推送失败");
            }
        }

        private static HertaLinkMessage BuildMessage(
            NanaEnvelope<SendMessageEvent> envelope,
            SendMessageEvent evt,
            VivContextContent context)
        {
            var isGroup = evt.ReceiverType == EmChatReceiverType.Group;

            return new HertaLinkMessage
            {
                // 落库前的旧事件没有 MessageId，退回信封 Id（至少不丢推）
                MessageId = evt.MessageId > 0 ? evt.MessageId : envelope.MessageId,
                SentAt = evt.SentAt,
                TenantId = context.SubjectId,
                AppId = context.AppId > 0 ? context.AppId : null,
                FromUserId = evt.FromUserId == 0 ? 0 : context.UserId,
                ToUserId = isGroup ? 0 : evt.TargetId,
                GroupId = isGroup ? evt.TargetId : null,
                ReceiverType = evt.ReceiverType,
                MessageType = evt.MessageType,
                Body = HertaMagic.GetChatMessage(evt.MessageType, evt.Message),
                TraceId = context.TraceId
            };
        }
    }
}
