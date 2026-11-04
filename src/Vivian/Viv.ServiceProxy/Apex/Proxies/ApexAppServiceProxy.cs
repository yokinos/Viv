using Viv.Log;
using Viv.ServiceProxy.Apex.Ports;
using Viv.ServiceProxy.Protos;

namespace Viv.ServiceProxy.Apex.Proxies
{
    /// <summary>
    /// Apex 应用代理。包住 gRPC 客户端和准入缓存；代理是 Scoped，缓存是单例。
    /// </summary>
    internal sealed class ApexAppServiceProxy : IProxyApexAppService
    {
        private readonly ApexAppQuery.ApexAppQueryClient _client;
        private readonly AppCheckCache _cache;
        private readonly ILoggerContract _logger;
        private readonly TimeSpan _checkTtl;

        public ApexAppServiceProxy(
            ApexAppQuery.ApexAppQueryClient client,
            AppCheckCache cache,
            ProxyEndpointOption option,
            ILoggerContract logger)
        {
            _client = client;
            _cache = cache;
            _logger = logger;
            _checkTtl = TimeSpan.FromSeconds(Math.Max(1, option.CacheSeconds));
        }

        public async Task<AppCheckResult> CheckAsync(long clientAppId, CancellationToken cancellationToken = default)
        {
            if (clientAppId <= 0)
                return AppCheckResult.Denied("缺少 AppId");

            if (_cache.TryGet(clientAppId, out var cached))
                return cached;

            try
            {
                var reply = await _client
                    .ValidateClientAppAsync(
                        new ValidateClientAppRequest { AppId = clientAppId },
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var result = reply is { Exists: true, Enabled: true }
                    ? AppCheckResult.Ok()
                    : AppCheckResult.Denied(string.IsNullOrWhiteSpace(reply.Message) ? "客户端不可用" : reply.Message);

                _cache.Set(clientAppId, result, _checkTtl);
                return result;
            }
            catch (Exception ex)
            {
                // 依赖挂了也不放行
                _logger.Error($"校验 AppId={clientAppId} 失败", ex);
                return AppCheckResult.Denied("客户端校验服务不可用");
            }
        }

        public async Task<AppPage> GetAppPageAsync(AppPageRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            // 服务端还会再收敛，这里先挡明显非法值
            var index = Math.Max(1, request.PageIndex);
            var size = Math.Clamp(request.PageSize, 1, 200);

            try
            {
                var reply = await _client
                    .GetClientAppPageAsync(
                        new GetClientAppPageRequest { PageIndex = index, PageSize = size },
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var items = reply.Items
                    .Select(x => new AppItem(x.Id, x.AppKey, x.Name, x.Enabled))
                    .ToList();

                return new AppPage(items, reply.TotalCount, reply.TotalPages);
            }
            catch (Exception ex)
            {
                // 带上页码，排障时能看出卡在哪一页
                _logger.Error($"取应用分页失败: page={index} size={size}", ex);
                throw;
            }
        }
    }

    /// <summary>准入校验结果的进程内缓存。条目按 TTL 判断有效，过期后惰性覆盖</summary>
    internal sealed class AppCheckCache
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<long, Entry> _entries = new();

        public bool TryGet(long clientAppId, out AppCheckResult result)
        {
            if (_entries.TryGetValue(clientAppId, out var entry) && entry.ExpireAt > DateTimeOffset.UtcNow)
            {
                result = entry.Result;
                return true;
            }

            result = default;
            return false;
        }

        public void Set(long clientAppId, AppCheckResult result, TimeSpan ttl)
            => _entries[clientAppId] = new Entry(result, DateTimeOffset.UtcNow.Add(ttl));

        private readonly record struct Entry(AppCheckResult Result, DateTimeOffset ExpireAt);
    }
}
