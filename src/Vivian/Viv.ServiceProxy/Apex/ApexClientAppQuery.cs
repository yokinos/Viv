using Microsoft.Extensions.Options;
using Viv.Log;
using Viv.ServiceProxy.Protos;

namespace Viv.ServiceProxy.Apex
{
    /// <summary>
    /// 走 gRPC 问 Apex：这个 AppId 存不存在、有没有被禁用。
    /// 每条连接都会问一次，结果在 <see cref="ClientAppCheckCache"/> 里缓存一段时间；问不到就不放行。
    /// </summary>
    public sealed class ApexClientAppQuery : IClientAppQuery
    {
        private readonly ApexAppQuery.ApexAppQueryClient _client;
        private readonly ClientAppCheckCache _cache;
        private readonly ILoggerContract _logger;
        private readonly TimeSpan _ttl;

        public ApexClientAppQuery(
            ApexAppQuery.ApexAppQueryClient client,
            ClientAppCheckCache cache,
            IOptions<ApexClientAppOption> options,
            ILoggerContract logger)
        {
            _client = client;
            _cache = cache;
            _logger = logger;
            _ttl = TimeSpan.FromSeconds(Math.Max(1, options.Value.CacheSeconds));
        }

        public async Task<ClientAppCheck> ValidateAsync(long appId, CancellationToken cancellationToken = default)
        {
            if (appId <= 0)
                return ClientAppCheck.Denied("缺少 AppId");

            if (_cache.TryGet(appId, out var cached))
                return cached;

            try
            {
                var reply = await _client
                    .ValidateClientAppAsync(new ValidateClientAppRequest { AppId = appId }, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var check = reply is { Exists: true, Enabled: true }
                    ? ClientAppCheck.Ok()
                    : ClientAppCheck.Denied(string.IsNullOrWhiteSpace(reply.Message) ? "客户端不可用" : reply.Message);

                _cache.Set(appId, check, _ttl);
                return check;
            }
            catch (Exception ex)
            {
                // Apex 挂了也不放行：宁可连不上，也不要让未知客户端混进来
                _logger.Error($"校验 AppId={appId} 失败", ex);
                return ClientAppCheck.Denied("客户端校验服务不可用");
            }
        }
    }
}
