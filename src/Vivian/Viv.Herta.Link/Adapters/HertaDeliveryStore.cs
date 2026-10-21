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
    /// 投递确认落在 Redis：每个用户一个 Hash，记水位（字段 w）和最近确认过的消息 Id（字段名就是 Id）。
    ///
    /// Id 记到 <see cref="KinnaOptions.DeliveryIdLimit"/> 条就清空，只留水位，相当于「全部标记已收到」；
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
            var state = await GetDeliveredAsync(subjectId, userId, cancellationToken).ConfigureAwait(false);
            var watermark = state.Watermark ?? 0;

            if (messageIds.Count == 0)
                return watermark;

            // 水位只进不退：客户端重连后乱序补报，不能把水位往回拨
            watermark = Math.Max(watermark, messageIds.Max());
            var key = _session.Keys.DeliveryKey(subjectId, userId);
            var idLimit = _session.Options.DeliveryIdLimit;

            try
            {
                // 配 0 表示从不记 Id，只留水位
                if (idLimit > 0)
                {
                    foreach (var id in messageIds)
                    {
                        await _redis.HashSetAsync(key, id.ToString(CultureInfo.InvariantCulture), 1).ConfigureAwait(false);
                    }
                }

                await _redis.HashSetAsync(key, WatermarkField, watermark).ConfigureAwait(false);

                if (idLimit > 0)
                {
                    var all = await _redis.HashGetAllAsync<long>(key).ConfigureAwait(false);
                    if (all.Count - 1 > idLimit)
                    {
                        // 记太多就没意义了，全清掉只留水位
                        await _redis.HashRemoveAsync(key, [.. all.Keys.Where(x => x != WatermarkField)]).ConfigureAwait(false);
                    }
                }

                await _redis.SetKeyExpireAsync(key, Retention).ConfigureAwait(false);
                return watermark;
            }
            catch (Exception ex)
            {
                // 记不上不影响客户端：它下次 Resync 会拿到空状态，从而被要求补拉
                _logger.Warning($"投递确认写入失败: subject={subjectId} user={userId} watermark={watermark} {ex.Message}");
                return state.Watermark ?? 0;
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
