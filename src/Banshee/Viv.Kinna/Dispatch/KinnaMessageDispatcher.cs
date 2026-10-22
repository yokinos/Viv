using Microsoft.AspNetCore.SignalR;
using Viv.Kinna.Connections;
using Viv.Kinna.Metrics;
using Viv.Log;

namespace Viv.Kinna.Dispatch
{
    /// <summary>
    /// 通用投递流程。业务只写 IKinnaPusher，这里负责校验计划、查连接、发送和记账。
    /// </summary>
    public sealed class KinnaMessageDispatcher<THub, TEvent> : IKinnaMessageDispatcher<THub, TEvent>
        where THub : Hub
    {
        private readonly IKinnaSession<THub> _session;
        private readonly IKinnaPusher<TEvent> _pusher;
        private readonly ILoggerContract _logger;

        public KinnaMessageDispatcher(IKinnaSession<THub> session, IKinnaPusher<TEvent> pusher, ILoggerContract logger)
        {
            _session = session;
            _pusher = pusher;
            _logger = logger;
        }

        public async Task<KinnaPushOutcome> DispatchAsync(
            TEvent evt,
            KinnaPushContext? context,
            CancellationToken cancellationToken = default)
        {
            KinnaPushPlan? plan;
            try
            {
                plan = _pusher.Plan(evt, context);
            }
            catch (Exception ex)
            {
                _logger.Error($"[{_session.Hub}] 生成推送计划失败", ex);
                KinnaMetrics.RecordDispatch(_session.Hub, "unknown", "failed");
                return KinnaPushOutcome.Failed;
            }

            if (plan is null)
            {
                KinnaMetrics.RecordDispatch(_session.Hub, "unknown", "dropped");
                return KinnaPushOutcome.Dropped;
            }

            // 代发是正常场景，和调用者对不上只留一条审计
            if (plan.AuthorId != 0 && context != null && plan.AuthorId != context.UserId)
            {
                _logger.Info(
                    $"以他人名义发送: hub={_session.Hub} 调用者={context.UserId} 署名={plan.AuthorId} " +
                    $"租户={plan.Target.SubjectId} 传输Id={context.TransportMessageId}");
                KinnaMetrics.RecordIdentityOverride(_session.Hub);
            }

            try
            {
                if (plan.Target.Kind == KinnaTargetKind.Group)
                {
                    if (string.IsNullOrEmpty(plan.Target.GroupName))
                    {
                        _logger.Warning($"[{_session.Hub}] 群推送缺少组名，已丢弃: kind={plan.Kind}");
                        KinnaMetrics.RecordDispatch(_session.Hub, plan.Kind, "dropped");
                        return KinnaPushOutcome.Dropped;
                    }

                    await _session.Sender
                        .SendToGroupAsync(plan.Target.GroupName!, plan.Method, plan.Payload, cancellationToken)
                        .ConfigureAwait(false);

                    KinnaMetrics.RecordDispatch(_session.Hub, plan.Kind, "ok");
                    return KinnaPushOutcome.Success;
                }

                if (plan.Target.SubjectId <= 0 || plan.Target.UserId <= 0)
                {
                    _logger.Warning($"[{_session.Hub}] 单聊推送缺少租户或用户，已丢弃: kind={plan.Kind}");
                    KinnaMetrics.RecordDispatch(_session.Hub, plan.Kind, "dropped");
                    return KinnaPushOutcome.Dropped;
                }

                var connectionIds = _session.Pool.GetConnectionIds(plan.Target.SubjectId, plan.Target.UserId);
                if (connectionIds.Count == 0)
                {
                    // 每个副本都收到全量消息，查不到就是人没连在这台机器上
                    KinnaMetrics.RecordDispatch(_session.Hub, plan.Kind, "skipped");
                    return KinnaPushOutcome.Skipped;
                }

                await _session.Sender
                    .SendToConnectionsAsync(connectionIds, plan.Method, plan.Payload, cancellationToken)
                    .ConfigureAwait(false);

                KinnaMetrics.RecordDispatch(_session.Hub, plan.Kind, "ok");
                return KinnaPushOutcome.Success;
            }
            catch (Exception ex)
            {
                _logger.Error($"[{_session.Hub}] 推送失败: kind={plan.Kind} method={plan.Method}", ex);
                KinnaMetrics.RecordDispatch(_session.Hub, plan.Kind, "failed");
                return KinnaPushOutcome.Failed;
            }
        }
    }
}
