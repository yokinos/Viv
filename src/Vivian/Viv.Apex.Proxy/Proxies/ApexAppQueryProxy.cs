using Viv.Apex.Proxy.Contracts;
using Viv.Apex.Proxy.Ports;
using Viv.Log;

namespace Viv.Apex.Proxy.Proxies
{
    /// <summary>
    /// Apex 应用代理：把 MagicOnion 客户端与失败语义收在一处。
    /// 客户端本身是单例（channel 复用），代理按作用域注册。
    ///
    /// 不在这里做准入缓存：Apex 侧的 <c>ClientAppRepository.GetAppAsync</c> 已经走
    /// <c>DataAccessCacheBase</c>（Redis + 回源），再叠一层进程内缓存只会让“客户端被禁用”
    /// 这段延迟变成两段 TTL 相加。
    /// </summary>
    internal sealed class ApexAppQueryProxy : IProxyApexAppService
    {
        private readonly IApexAppQueryService _client;
        private readonly ILoggerContract _logger;

        public ApexAppQueryProxy(IApexAppQueryService client, ILoggerContract logger)
        {
            _client = client;
            _logger = logger;
        }

        public Task<AppCheckResult> CheckAppAsync(long clientAppId, CancellationToken cancellationToken = default)
        {
            // UserType 保持契约默认值 -1：明确告诉服务端「只查应用，不查归属」
            return SendAsync(new ValidateClientAppRequest { AppId = clientAppId }, clientAppId, cancellationToken);
        }

        public Task<AppCheckResult> CheckAsync(long subjectId, long clientAppId, int userType, CancellationToken cancellationToken = default)
        {
            // 缺归属主体就不发 RPC：服务端也会拒，但没必要为一次必然失败的调用付网络往返
            if (subjectId <= 0)
                return Task.FromResult(AppCheckResult.Denied("缺少归属主体"));

            return SendAsync(
                new ValidateClientAppRequest { AppId = clientAppId, SubjectId = subjectId, UserType = userType },
                clientAppId,
                cancellationToken);
        }

        private async Task<AppCheckResult> SendAsync(ValidateClientAppRequest request, long clientAppId, CancellationToken cancellationToken)
        {
            if (clientAppId <= 0)
                return AppCheckResult.Denied("缺少 AppId");

            try
            {
                // MagicOnion 的 UnaryResult 不接受 CancellationToken，取消由客户端超时兜底。
                var reply = await _client.ValidateClientAppAsync(request).ConfigureAwait(false);

                return reply is { Exists: true, Enabled: true, Allowed: true }
                    ? AppCheckResult.Ok()
                    : AppCheckResult.Denied(string.IsNullOrWhiteSpace(reply.Message) ? "客户端不可用" : reply.Message);
            }
            catch (Exception ex)
            {
                // 依赖挂了也不放行：宁可连不上，也不要让未知客户端混进来
                _logger.Error($"校验 AppId={clientAppId} 失败", ex);
                return AppCheckResult.Denied("客户端校验服务不可用");
            }
        }

        public async Task<AppPage> GetAppPageAsync(AppPageRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            // 服务端还会再收敛一次；这里先挡住明显的非法值，避免发出去才发现
            var index = Math.Max(1, request.PageIndex);
            var size = Math.Clamp(request.PageSize, 1, 200);

            try
            {
                var reply = await _client
                    .GetClientAppPageAsync(new GetClientAppPageRequest { PageIndex = index, PageSize = size })
                    .ConfigureAwait(false);

                var items = reply.Items
                    .Select(x => new AppItem(x.Id, x.AppKey, x.Name, x.Enabled))
                    .ToList();

                return new AppPage(items, reply.TotalCount, reply.TotalPages);
            }
            catch (Exception ex)
            {
                // 批处理路径不该拿到一个裸 RpcException：至少带上"第几页"的上下文
                _logger.Error($"取应用分页失败: page={index} size={size}", ex);
                throw;
            }
        }
    }
}
