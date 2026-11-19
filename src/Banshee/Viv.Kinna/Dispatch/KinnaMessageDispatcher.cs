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
                // 发起这次请求的那个客户端。它已经通过接口返回拿到了结果，不再吃推送，
                // 这样每条消息对每条连接至多一次，客户端不用去重
                var excluded = ResolveSenderClientConnections(plan, context, plan.Target.SubjectId);

                if (plan.Target.Kind == KinnaTargetKind.Group)
                {
                    if (plan.Target.SubjectId <= 0 || plan.Target.GroupId <= 0)
                    {
                        _logger.Warning(
                            $"[{_session.Hub}] 群推送缺少租户或群 Id，已丢弃: kind={plan.Kind} " +
                            $"subject={plan.Target.SubjectId} group={plan.Target.GroupId}");
                        KinnaMetrics.RecordDispatch(_session.Hub, plan.Kind, "dropped");
                        return KinnaPushOutcome.Dropped;
                    }

                    // 组名只在这里生成：业务只给租户和群 Id，拼错了不会静默发错人
                    var groupName = _session.Keys.GroupName(plan.Target.SubjectId, plan.Target.GroupId);

                    // 发送方的其它端不用另外补投：它们本来就是群成员，这次组播就覆盖了。
                    // 只需要把发起端剔掉，否则发送的这台设备会收到自己刚发的消息
                    await _session.Sender
                        .SendToGroupAsync(groupName, plan.Method, plan.Payload, excluded.Count == 0 ? null : excluded, cancellationToken)
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

                var targets = CollectTargets(plan, context, excluded);
                if (targets.Count == 0)
                {
                    // 每个副本都收到全量消息，查不到就是人没连在这台机器上
                    KinnaMetrics.RecordDispatch(_session.Hub, plan.Kind, "skipped");
                    return KinnaPushOutcome.Skipped;
                }

                await _session.Sender
                    .SendToConnectionsAsync(targets, plan.Method, plan.Payload, cancellationToken)
                    .ConfigureAwait(false);

                KinnaMetrics.RecordDispatch(_session.Hub, plan.Kind, "ok");
                return KinnaPushOutcome.Success;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 主机停机 / 请求取消不是「推送失败」。记成 failed 会让消费者去重投，
                // 而实际是没人再收这条消息了——停机期间刷一片 failed 指标还会淹掉真故障。
                // 原样抛出去，交给消费者自己的取消处理
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error($"[{_session.Hub}] 推送失败: kind={plan.Kind} method={plan.Method}", ex);
                KinnaMetrics.RecordDispatch(_session.Hub, plan.Kind, "failed");
                return KinnaPushOutcome.Failed;
            }
        }

        /// <summary>
        /// 单聊的实际投递目标：接收方 + 发送方的其它端，剔掉发起端，同一条连接只留一份。
        ///
        /// 自己给自己发时「接收方」和「发送方」是同一批连接，两路查出来会重叠，
        /// 所以必须按连接 Id 去重——否则那台设备会收到两条一模一样的消息。
        /// 这一层去重是服务端的事：客户端只管按收到的帧渲染，不需要自己认哪条是重复的。
        /// </summary>
        private List<string> CollectTargets(KinnaPushPlan plan, KinnaPushContext? context, List<string> excluded)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var targets = new List<string>();

            void Add(List<string> connectionIds)
            {
                foreach (var connectionId in connectionIds)
                {
                    if (seen.Add(connectionId))
                    {
                        targets.Add(connectionId);
                    }
                }
            }

            Add(_session.Pool.GetConnectionIds(plan.Target.SubjectId, plan.Target.UserId));

            // 群聊不走这里：发送方的端本来就在组里，组播已经覆盖
            if (plan.MirrorToSender)
            {
                // 代发时署名人和调用者不是同一个：要同步的是署名人的那台设备
                var authorId = plan.AuthorId > 0 ? plan.AuthorId : context?.UserId ?? 0;
                if (authorId > 0 && authorId != plan.Target.UserId)
                {
                    Add(_session.Pool.GetConnectionIds(plan.Target.SubjectId, authorId));
                }
            }

            if (excluded.Count > 0)
            {
                targets.RemoveAll(excluded.Contains);
            }

            return targets;
        }

        /// <summary>
        /// 找出「发起这次请求的那个客户端」在本机上的连接。
        ///
        /// 用 AppId + 用户 + 租户定位，不靠连接 Id：HTTP 请求和长连接可能落在不同副本上，
        /// 但每台副本只推本机连接，所以持有这条连接的副本查出来非空、由它把发起端剔掉；
        /// 不持有它的副本查出来是空的，本来也推不到那个端。
        ///
        /// 计划里关掉 <c>SkipSenderClient</c>、没有信封、或信封没带 AppId 时返回空，等于不排除任何人。
        /// </summary>
        private List<string> ResolveSenderClientConnections(KinnaPushPlan plan, KinnaPushContext? context, long subjectId)
        {
            if (!plan.SkipSenderClient || context is null)
            {
                return [];
            }

            if (context.AppId <= 0 || context.UserId <= 0 || subjectId <= 0)
            {
                return [];
            }

            return _session.Pool.GetConnectionIds(context.AppId, subjectId, context.UserId);
        }
    }
}
