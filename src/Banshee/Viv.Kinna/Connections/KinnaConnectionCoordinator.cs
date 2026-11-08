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
        private readonly TimeProvider _time;

        public KinnaConnectionCoordinator(
            IKinnaSession<THub> session,
            ILoggerContract logger,
            IKinnaGroupSource<THub>? groups = null,
            IKinnaPresenceSink<THub>? presence = null,
            IKinnaDeliveryStore<THub>? delivery = null,
            KinnaUplinkLimiter<THub>? limiter = null,
            TimeProvider? timeProvider = null)
        {
            _session = session;
            _logger = logger;
            _groups = groups;
            _presence = presence ?? NoopKinnaPresenceSink<THub>.Instance;
            _delivery = delivery ?? NoopKinnaDeliveryStore<THub>.Instance;
            _limiter = limiter;
            _time = timeProvider ?? TimeProvider.System;
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
                await SubscribeAsync(connectionId, identity, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _logger.Debug($"未注册 IKinnaGroupSource<{_session.Hub}>，本实例不投递群消息");
            }

            return new KinnaConnected
            {
                ConnectionId = connectionId,
                ServerTime = _time.GetUtcNow()
            };
        }

        /// <summary>
        /// 查群并加组。失败只丢群消息，不丢连接——连接此时已经登记好了，
        /// 单聊和按人推送都还正常；把异常抛出去等于让群服务决定 IM 能不能连上。
        /// </summary>
        private async Task SubscribeAsync(string connectionId, VivContextContent identity, CancellationToken cancellationToken)
        {
            try
            {
                var groupIds = await _groups!
                    .GetUserGroupIdsAsync(identity.SubjectId, identity.UserId, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var groupId in groupIds)
                {
                    await _session.Sender
                        .AddToGroupAsync(connectionId, _session.Keys.GroupName(identity.SubjectId, groupId), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 连接已经中止，交给端点/Hub 的收尾路径注销
                throw;
            }
            catch (Exception ex)
            {
                KinnaMetrics.RecordGroupSubscriptionFailed(_session.Hub);
                _logger.Warning(
                    $"[{_session.Hub}] 查询群成员失败，本次不加组: subject={identity.SubjectId} user={identity.UserId} {ex.Message}");
            }
        }

        public async Task UnregisterAsync(string connectionId, CancellationToken cancellationToken = default)
        {
            // 先摘限流窗口再判连接是否还在：连接早被清扫或别的副本踢掉时 Pool.Remove 会返回 null，
            // 那个分支以前直接 return，窗口就留在表里没人回收
            _limiter?.Release(connectionId);

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

            _limiter?.Release(connectionId);

            await _presence.OfflineAsync(info.Value.SubjectId, info.Value.UserId).ConfigureAwait(false);

            // 走 Terminate：原生连接发完软踢帧就硬断，否则旧连接会一直挂在那儿收不到消息
            await _session.Sender
                .TerminateConnectionAsync(connectionId, KinnaClientMethods.ForceDisconnect, [], cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<KinnaPong> PingAsync(string connectionId, CancellationToken cancellationToken = default)
        {
            var now = _time.GetUtcNow();

            // 被限流就不续租：客户端下一个窗口再 Ping 一次就恢复了，没必要在这里阻塞或抛错
            if (_limiter?.TryAcquire(connectionId) == false)
            {
                KinnaMetrics.RecordUplinkThrottled(_session.Hub, "Ping");
                return new KinnaPong { ServerTime = now, LeaseExpiresAt = now + _leaseTtl };
            }

            var info = _session.Pool.TryGet(connectionId);
            if (info == null)
            {
                // 表里已经没有这条连接（超租约被清扫、被别的副本踢掉）。
                // 以前这里照常回一个 now + ttl 的租约，客户端以为还活着，实际已经收不到任何消息——
                // 现在明确告诉它「这条不算数」，端点收到后会直接关掉 socket，客户端重连重新登记。
                return new KinnaPong { ServerTime = now, LeaseExpiresAt = now, Alive = false };
            }

            _session.Pool.Touch(connectionId);
            await _presence.RenewAsync(info.Value.SubjectId, info.Value.UserId).ConfigureAwait(false);

            return new KinnaPong
            {
                ServerTime = now,
                LeaseExpiresAt = now + _leaseTtl
            };
        }

        public async Task<KinnaAckResult> AckAsync(string connectionId, IReadOnlyList<long> messageIds, CancellationToken cancellationToken = default)
        {
            if (_limiter?.TryAcquire(connectionId) == false)
            {
                // 丢一次确认不会丢消息：客户端看到 Throttled 稍后重报，或者由 Resync 触发补拉
                KinnaMetrics.RecordUplinkThrottled(_session.Hub, "Ack");
                return new KinnaAckResult { Status = KinnaAckStatus.Throttled };
            }

            // 身份从连接表拿：客户端不能自己报 userId，否则可以替别人确认
            var info = _session.Pool.TryGet(connectionId);
            if (info == null)
            {
                _logger.Debug($"投递确认来自未知连接，已忽略: {connectionId}");
                return new KinnaAckResult { Status = KinnaAckStatus.UnknownConnection };
            }

            try
            {
                var watermark = await _delivery
                    .AckAsync(info.Value.SubjectId, info.Value.UserId, messageIds, cancellationToken)
                    .ConfigureAwait(false);

                return new KinnaAckResult { Status = KinnaAckStatus.Accepted, Watermark = watermark };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 不往外抛：抛出去在 SignalR 里只是一次调用异常，客户端拿不到「该重报」这个信号；
                // 而归到 Failed 后客户端下一轮补报，消息照样对齐
                KinnaMetrics.RecordAckFailed(_session.Hub);
                _logger.Error($"[{_session.Hub}] 记录投递确认失败: connection={connectionId}", ex);
                return new KinnaAckResult { Status = KinnaAckStatus.Failed };
            }
        }

        public async Task<KinnaResyncResult> ResyncAsync(string connectionId, long? lastMessageId, CancellationToken cancellationToken = default)
        {
            var now = _time.GetUtcNow();

            // Resync 是上行里最贵的一条：一次存储读，客户端拿到结果后往往还跟一次 HTTP 补拉。
            // 重连风暴时恰恰是它最先被打爆，所以和其它上行指令共用同一份配额
            if (_limiter?.TryAcquire(connectionId) == false)
            {
                KinnaMetrics.RecordUplinkThrottled(_session.Hub, "Resync");

                // 明确告诉客户端「这次什么都没查」，并且不给补拉信号：
                // 否则客户端会立刻去打 HTTP，把压力原地转给数据库
                return new KinnaResyncResult
                {
                    Throttled = true,
                    NeedBackfill = false,
                    ServerTime = now
                };
            }

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
                ServerTime = now
            };
        }

        public async Task KickAsync(string connectionId, CancellationToken cancellationToken = default)
        {
            // 和清扫同一条路：先摘表再扣在线态，最后软踢。
            // OnDisconnected 随后 Unregister 时表里已经没有这条，不会再扣一次。
            var info = _session.Pool.Remove(connectionId);
            if (info != null)
            {
                _limiter?.Release(connectionId);
                await _presence.OfflineAsync(info.Value.SubjectId, info.Value.UserId).ConfigureAwait(false);
            }

            // 即使表里没有也要断：可能是只在传输层登记过的连接，不硬断就再也回收不了
            await _session.Sender
                .TerminateConnectionAsync(connectionId, KinnaClientMethods.ForceDisconnect, [], cancellationToken)
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
