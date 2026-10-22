using Microsoft.AspNetCore.SignalR;
using Viv.Contracts.Models;
using Viv.Kinna.Contracts;
using Viv.Kinna.Metrics;
using Viv.Kinna.Ports;
using Viv.Log;

namespace Viv.Kinna.Connections
{
    /// <summary>
    /// 连接生命周期实现：写连接表、续租、加组，都是本地操作。
    /// 跨副本的动作（消息广播、成员变更）不在这里。
    /// </summary>
    public sealed class KinnaConnectionCoordinator<THub> : IKinnaConnectionCoordinator<THub> where THub : Hub
    {
        private readonly IKinnaSession<THub> _session;
        private readonly IKinnaGroupSource<THub>? _groups;
        private readonly IKinnaPresenceSink<THub> _presence;
        private readonly IKinnaDeliveryStore<THub> _delivery;
        private readonly KinnaUplinkLimiter<THub>? _limiter;
        private readonly ILoggerContract _logger;
        private readonly TimeSpan _leaseTtl;

        public KinnaConnectionCoordinator(
            IKinnaSession<THub> session,
            ILoggerContract logger,
            IKinnaGroupSource<THub>? groups = null,
            IKinnaPresenceSink<THub>? presence = null,
            IKinnaDeliveryStore<THub>? delivery = null,
            KinnaUplinkLimiter<THub>? limiter = null)
        {
            _session = session;
            _logger = logger;
            _groups = groups;
            _presence = presence ?? NoopKinnaPresenceSink<THub>.Instance;
            _delivery = delivery ?? NoopKinnaDeliveryStore<THub>.Instance;
            _limiter = limiter;
            _leaseTtl = TimeSpan.FromSeconds(Math.Max(1, session.Options.EntryTtlSeconds));
        }

        public async Task<KinnaConnected?> RegisterAsync(
            string connectionId,
            VivContextContent identity,
            CancellationToken cancellationToken = default)
        {
            if (identity.SubjectId <= 0 || identity.UserId <= 0)
                return null;

            // 同一个客户端只留一条连接：新连接顶掉旧的，否则重装/重连会留下幽灵连接，
            // 推送时同一条消息会被发到已经死掉的连接上
            if (_session.Options.SingleConnectionPerApp)
            {
                var existing = _session.Pool.GetConnectionIds(identity.AppId, identity.SubjectId, identity.UserId);
                foreach (var oldConnectionId in existing)
                {
                    if (oldConnectionId == connectionId) continue;
                    await ReplaceAsync(oldConnectionId, cancellationToken).ConfigureAwait(false);
                }
            }

            _session.Pool.Add(connectionId, identity.AppId, identity.SubjectId, identity.UserId);
            await _presence.OnlineAsync(identity.SubjectId, identity.UserId).ConfigureAwait(false);

            // 组是本进程的，所以只有持有这条连接的副本需要加组
            if (_groups != null)
            {
                var groupIds = await _groups.GetUserGroupIdsAsync(identity.SubjectId, identity.UserId).ConfigureAwait(false);
                foreach (var groupId in groupIds)
                {
                    await _session.Sender
                        .AddToGroupAsync(connectionId, _session.Keys.GroupName(identity.SubjectId, groupId), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            else
            {
                _logger.Debug($"未注册 IKinnaGroupSource<{_session.Hub}>，本实例不投递群消息");
            }

            return new KinnaConnected
            {
                ConnectionId = connectionId,
                ServerTime = DateTimeOffset.UtcNow
            };
        }

        public async Task UnregisterAsync(string connectionId, CancellationToken cancellationToken = default)
        {
            // 摘除和取身份是一步。分两步的话，中间被清扫抢走就会两边各扣一次在线态
            var info = _session.Pool.Remove(connectionId);
            if (info == null) return;

            await _presence.OfflineAsync(info.Value.SubjectId, info.Value.UserId).ConfigureAwait(false);
        }

        /// <summary>同客户端重复登录时先摘旧连接：扣在线态、发软踢，最后让 OnDisconnectedAsync 空转。</summary>
        private async Task ReplaceAsync(string connectionId, CancellationToken cancellationToken)
        {
            var info = _session.Pool.Remove(connectionId);
            if (info == null) return;

            _logger.Info($"同一客户端重复登录，踢掉旧连接: {connectionId} app={info.Value.AppId}");

            await _presence.OfflineAsync(info.Value.SubjectId, info.Value.UserId).ConfigureAwait(false);
            await _session.Sender
                .SendToConnectionAsync(connectionId, KinnaClientMethods.ForceDisconnect, [], cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<KinnaPong> PingAsync(string connectionId, CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;

            // 被限流就不续租：客户端下一个窗口再 Ping 一次就恢复了，没必要在这里阻塞或抛错
            if (_limiter?.TryAcquire(connectionId) == false)
            {
                KinnaMetrics.RecordUplinkThrottled(_session.Hub, "Ping");
                return new KinnaPong { ServerTime = now, LeaseExpiresAt = now + _leaseTtl };
            }

            var info = _session.Pool.TryGet(connectionId);
            _session.Pool.Touch(connectionId);

            if (info != null)
                await _presence.RenewAsync(info.Value.SubjectId, info.Value.UserId).ConfigureAwait(false);

            return new KinnaPong
            {
                ServerTime = now,
                LeaseExpiresAt = now + _leaseTtl
            };
        }

        public async Task<long> AckAsync(string connectionId, IReadOnlyList<long> messageIds, CancellationToken cancellationToken = default)
        {
            if (_limiter?.TryAcquire(connectionId) == false)
            {
                // 丢一次确认不会丢消息：客户端下次补报，或者由 Resync 触发补拉
                KinnaMetrics.RecordUplinkThrottled(_session.Hub, "Ack");
                return 0;
            }

            // 身份从连接表拿：客户端不能自己报 userId，否则可以替别人确认
            var info = _session.Pool.TryGet(connectionId);
            if (info == null)
            {
                _logger.Debug($"投递确认来自未知连接，已忽略: {connectionId}");
                return 0;
            }

            return await _delivery
                .AckAsync(info.Value.SubjectId, info.Value.UserId, messageIds, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<KinnaResyncResult> ResyncAsync(string connectionId, long? lastMessageId, CancellationToken cancellationToken = default)
        {
            var info = _session.Pool.TryGet(connectionId);
            var state = info == null
                ? KinnaDeliveryState.Empty
                : await _delivery
                    .GetDeliveredAsync(info.Value.SubjectId, info.Value.UserId, cancellationToken)
                    .ConfigureAwait(false);

            // 服务端记录的水位比客户端持有的还大，说明客户端可能漏了；没有水位就按「需要补拉」处理，
            // 增量拉取是幂等的，多拉一次比漏消息便宜
            var needBackfill = lastMessageId is null || state.Watermark is null || state.Watermark > lastMessageId;

            return new KinnaResyncResult
            {
                LastDeliveredMessageId = state.Watermark,
                DeliveredMessageIds = [.. state.MessageIds],
                NeedBackfill = needBackfill,
                ServerTime = DateTimeOffset.UtcNow
            };
        }

        public async Task KickAsync(string connectionId, CancellationToken cancellationToken = default)
        {
            _session.Pool.Remove(connectionId);
            await _session.Sender
                .SendToConnectionAsync(connectionId, KinnaClientMethods.ForceDisconnect, [], cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task KickUserAsync(long subjectId, long userId, CancellationToken cancellationToken = default)
        {
            var ids = _session.Pool.GetConnectionIds(subjectId, userId);
            foreach (var id in ids)
            {
                await KickAsync(id, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task KickSubjectAsync(long subjectId, CancellationToken cancellationToken = default)
        {
            var connections = _session.Pool.GetConnections(subjectId);
            foreach (var connection in connections)
            {
                await KickAsync(connection.ConnectionId, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
