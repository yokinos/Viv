using StackExchange.Redis;
using Viv.Herta.Link.Delivery;
using Viv.Herta.Link.Hubs;
using Viv.Kinna.Connections;
using Viv.Kinna.Options;
using Viv.Kinna.Ports;
using Viv.Log;
using Viv.Redis;

namespace Viv.Herta.Link.Adapters
{
    /// <summary>
    /// 投递确认落在 Redis：每个用户一个 Hash，字段 w 是已确认到的位置，字段 ids 是逗号分隔的
    /// 消息 Id 列表（记到 <see cref="KinnaOptions.DeliveryIdLimit"/> 条就清空，只留 w）。
    ///
    /// 读写在 Lua 里合成一步，多端并发 Ack 不会互相覆盖；一次 Ack 只走一个往返，
    /// 与确认的条数无关。不记明细：哪条消息被谁读了属于核心库，客户端走 HTTP 的已读接口。
    /// </summary>
    public sealed class HertaDeliveryStore : IKinnaDeliveryStore<ChatHub>
    {
        private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

        private readonly IKinnaSession<ChatHub> _session;
        private readonly IRedisService _redis;
        private readonly ILoggerContract _logger;

        public HertaDeliveryStore(IKinnaSession<ChatHub> session, IRedisService redis, ILoggerContract logger)
        {
            _session = session;
            _redis = redis;
            _logger = logger;
        }

        public async Task<long> AckAsync(
            long subjectId,
            long userId,
            IReadOnlyList<long> messageIds,
            CancellationToken cancellationToken = default)
        {
            var key = _session.Keys.DeliveryKey(subjectId, userId);
            var idLimit = _session.Options.DeliveryIdLimit;

            // ARGV：上限、TTL 秒、本次确认的 Id
            var args = new List<object?> { idLimit, (int)Retention.TotalSeconds };
            args.AddRange(messageIds.Where(x => x > 0));

            try
            {
                var result = await _redis
                    .ExecuteScriptAsync(DeliveryStoreScripts.Ack, key, [.. args])
                    .ConfigureAwait(false);

                return (long)result;
            }
            catch (Exception ex)
            {
                // 记不上不影响客户端：它下次 Resync 会要求补拉
                _logger.Warning($"投递确认写入失败: subject={subjectId} user={userId} {ex.Message}");
                return 0;
            }
        }

        public async Task<KinnaDeliveryState> GetDeliveredAsync(
            long subjectId,
            long userId,
            CancellationToken cancellationToken = default)
        {
            var key = _session.Keys.DeliveryKey(subjectId, userId);

            try
            {
                var result = await _redis
                    .ExecuteScriptAsync(DeliveryStoreScripts.Read, key)
                    .ConfigureAwait(false);

                if (result.Resp2Type != ResultType.Array)
                    return KinnaDeliveryState.Empty;

                var values = (RedisResult[])result;
                var watermark = long.TryParse((string?)values[0], out var stored) && stored > 0 ? stored : (long?)null;
                var ids = DeliveryIds.Parse((string?)values[1]);

                watermark ??= ids.Length > 0 ? ids[^1] : null;

                return new KinnaDeliveryState(watermark, ids);
            }
            catch (Exception ex)
            {
                _logger.Warning($"投递状态读取失败: subject={subjectId} user={userId} {ex.Message}");
                return KinnaDeliveryState.Empty;
            }
        }
    }
}
