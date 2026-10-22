using System.Globalization;
using Viv.Herta.Link.Hubs;
using Viv.Kinna.Connections;
using Viv.Kinna.Options;
using Viv.Kinna.Ports;
using Viv.Log;
using Viv.Redis;

namespace Viv.Herta.Link.Adapters
{
    /// <summary>
    /// 投递确认落在 Redis：每个用户一个 Hash，记已确认位置（字段 w）和最近确认过的消息 Id（字段名就是 Id）。
    ///
    /// Id 记到 <see cref="KinnaOptions.DeliveryIdLimit"/> 条就清空，只留已确认位置，相当于「全部标记已收到」；
    /// 配 0 表示从不记 Id。不记明细：哪条消息被谁读了属于核心库，客户端走 HTTP 的已读接口。
    /// </summary>
    public sealed class HertaDeliveryStore : IKinnaDeliveryStore<ChatHub>
    {
        private const string WatermarkField = "w";

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

            Dictionary<string, long> stored;
            try
            {
                stored = await _redis.HashGetAllAsync<long>(key).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 读不到就当没记过：客户端下次 Resync 会被要求补拉，不影响连接
                _logger.Warning($"投递确认读取失败: subject={subjectId} user={userId} {ex.Message}");
                return 0;
            }

            // 水位只进不退：客户端重连后乱序补报，不能把已确认位置往回拨
            var watermark = stored.TryGetValue(WatermarkField, out var existing) && existing > 0 ? existing : 0;
            if (messageIds.Count == 0)
                return watermark;

            var newWatermark = Math.Max(watermark, messageIds.Max());
            var incoming = messageIds
                .Where(x => x > 0)
                .Select(x => x.ToString(CultureInfo.InvariantCulture))
                .Distinct()
                .ToList();

            var recorded = stored.Keys.Where(x => x != WatermarkField).ToList();
            var toAdd = incoming.Where(x => !stored.ContainsKey(x)).ToList();
            var total = recorded.Count + toAdd.Count;

            try
            {
                // 一次读里就把「要加哪些、要不要清空」算完，不再回读一次数条数
                if (idLimit > 0 && total <= idLimit)
                {
                    foreach (var field in toAdd)
                    {
                        await _redis.HashSetAsync(key, field, 1).ConfigureAwait(false);
                    }
                }
                else if (recorded.Count > 0)
                {
                    // 配了 0 或者加进去就超限：把 Id 全清掉，只留水位
                    await _redis.HashRemoveAsync(key, [.. recorded]).ConfigureAwait(false);
                }

                if (newWatermark != watermark)
                {
                    await _redis.HashSetAsync(key, WatermarkField, newWatermark).ConfigureAwait(false);
                }

                await _redis.SetKeyExpireAsync(key, Retention).ConfigureAwait(false);
                return newWatermark;
            }
            catch (Exception ex)
            {
                _logger.Warning($"投递确认写入失败: subject={subjectId} user={userId} watermark={newWatermark} {ex.Message}");
                return watermark;
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
                var all = await _redis.HashGetAllAsync<long>(key).ConfigureAwait(false);
                if (all.Count == 0)
                    return KinnaDeliveryState.Empty;

                long? watermark = all.TryGetValue(WatermarkField, out var stored) && stored > 0 ? stored : null;

                var messageIds = all.Keys
                    .Where(x => x != WatermarkField)
                    .Select(x => long.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0)
                    .Where(x => x > 0)
                    .Order()
                    .ToArray();

                watermark ??= messageIds.Length > 0 ? messageIds[^1] : null;

                return new KinnaDeliveryState(watermark, messageIds);
            }
            catch (Exception ex)
            {
                _logger.Warning($"投递状态读取失败: subject={subjectId} user={userId} {ex.Message}");
                return KinnaDeliveryState.Empty;
            }
        }
    }
}
