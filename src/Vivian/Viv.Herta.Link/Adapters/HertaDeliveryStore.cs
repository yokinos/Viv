using StackExchange.Redis;
using Viv.Herta.Core.IRepository;
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
    /// 投递确认：水位落库，明细落 Redis。
    ///
    /// 水位（每个用户已确认到哪条）写进 EtChatDelivery，重启、换 Redis、清缓存都不丢；
    /// 明细（最近确认过的 Id，供 Resync 让客户端跳过）放 Redis 一个 Hash，字段 ids 是逗号分隔的
    /// 列表，记到 <see cref="KinnaOptions.DeliveryIdLimit"/> 条就清空只留水位 —— 明细丢了大不了多拉几条。
    ///
    /// Redis 失败时 Ack 仍会写库，Resync 退化成"读得到水位、没有明细"，客户端补拉一次，
    /// 不会出现"确认过的东西又算没确认"。
    /// </summary>
    public sealed class HertaDeliveryStore : IKinnaDeliveryStore<ChatHub>
    {
        private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

        private readonly IKinnaSession<ChatHub> _session;
        private readonly IChatDeliveryRepository _delivery;
        private readonly IRedisService _redis;
        private readonly ILoggerContract _logger;

        public HertaDeliveryStore(
            IKinnaSession<ChatHub> session,
            IChatDeliveryRepository delivery,
            IRedisService redis,
            ILoggerContract logger)
        {
            _session = session;
            _delivery = delivery;
            _redis = redis;
            _logger = logger;
        }

        public async Task<long> AckAsync(
            long subjectId,
            long userId,
            IReadOnlyList<long> messageIds,
            CancellationToken cancellationToken = default)
        {
            var maxId = messageIds.Count == 0 ? 0 : messageIds.Max();

            // 先落库：这是重连后判断要不要补拉的唯一依据，Redis 挂了也得写进去
            long watermark = 0;
            if (maxId > 0)
            {
                try
                {
                    watermark = await _delivery.MergeAsync(subjectId, userId, maxId).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"投递水位写入失败: subject={subjectId} user={userId} {ex.Message}");
                }
            }

            // 再记明细（Redis 挂了不影响水位）
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

                return Math.Max(watermark, (long)result);
            }
            catch (Exception ex)
            {
                _logger.Warning($"投递明细写入失败: subject={subjectId} user={userId} {ex.Message}");
                return watermark;
            }
        }

        public async Task<KinnaDeliveryState> GetDeliveredAsync(
            long subjectId,
            long userId,
            CancellationToken cancellationToken = default)
        {
            long? watermark = null;

            try
            {
                var stored = await _delivery.GetAsync(subjectId, userId).ConfigureAwait(false);
                if (stored is > 0) watermark = stored;
            }
            catch (Exception ex)
            {
                _logger.Warning($"投递水位读取失败: subject={subjectId} user={userId} {ex.Message}");
            }

            var key = _session.Keys.DeliveryKey(subjectId, userId);
            long redisWatermark = 0;
            var ids = Array.Empty<long>();

            try
            {
                var result = await _redis
                    .ExecuteScriptAsync(DeliveryStoreScripts.Read, key)
                    .ConfigureAwait(false);

                if (result.Resp2Type == ResultType.Array)
                {
                    var values = (RedisResult[])result;
                    long.TryParse((string?)values[0], out redisWatermark);
                    ids = DeliveryIds.Parse((string?)values[1]);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"投递明细读取失败: subject={subjectId} user={userId} {ex.Message}");
            }

            if (redisWatermark > 0)
                watermark = watermark is null ? redisWatermark : Math.Max(watermark.Value, redisWatermark);

            if (watermark is null && ids.Length > 0)
                watermark = ids[^1];

            return new KinnaDeliveryState(watermark, ids);
        }
    }
}
